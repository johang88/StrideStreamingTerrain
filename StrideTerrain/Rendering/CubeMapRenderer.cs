using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.ComputeEffect.GGXPrefiltering;
using Stride.Rendering.ComputeEffect.LambertianPrefiltering;
using Stride.Rendering.Skyboxes;
using System;

namespace StrideTerrain.Rendering;

public class CubeMapRenderer : SceneRendererBase
{
    private static readonly ProfilingKey RenderCubeMapProfilingKey = new("CubeMapRenderer.RenderCubeMap");
    private static readonly ProfilingKey SpecularProbeProfilingKey = new("CubeMapRenderer.SpecularProbe");

    public static readonly PropertyKey<bool> IsRenderingCubemap = new("CubeMapRenderer.IsRenderingCubemap", typeof(RenderContext));

    private readonly RenderView _renderView = new();

    public ISceneRenderer? Child { get; set; }
    public RenderGroupMask RenderMask { get; set; } = RenderGroupMask.All;
    public int Resolution { get; set; } = 1024;

    private int _currentFace = 0; // Render one face per frame

    private Texture? _cubeMap = null;
    public Skybox? Skybox = null;

    private LambertianPrefilteringSH? _lamberFiltering;
    private RadiancePrefilteringGGX? _specularRadiancePrefilterGGX;

    private Texture? _cubeMapSpecular;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _cubeMap = Texture.NewCube(Context.GraphicsDevice, Resolution, PixelFormat.R16G16B16A16_Float, TextureFlags.ShaderResource);
        _cubeMap.DisposeBy(this);

        _cubeMapSpecular = Texture.NewCube(Context.GraphicsDevice, Resolution, MipMapCount.Auto, PixelFormat.R16G16B16A16_Float, TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);
        _cubeMapSpecular.DisposeBy(this);

        _lamberFiltering = new LambertianPrefilteringSH(Context);
        _lamberFiltering.DisposeBy(this);
        _lamberFiltering.RadianceMap = _cubeMap;

        _specularRadiancePrefilterGGX = new(Context);
        _specularRadiancePrefilterGGX.DisposeBy(this);

        _specularRadiancePrefilterGGX.RadianceMap = _cubeMap;
        _specularRadiancePrefilterGGX.PrefilteredRadiance = _cubeMapSpecular;
        _specularRadiancePrefilterGGX.SamplingsCount = 64;
    }

    protected override void CollectCore(RenderContext context)
    {
        base.CollectCore(context);

        _currentFace++;
        if (_currentFace >= 6)
        {
            _currentFace = -1;
            return;
        }

        var inverseViewMatrix = Matrix.Invert(context.RenderView.View);
        var eye = inverseViewMatrix.Row4;
        var cameraPosition = new Vector3(eye.X, eye.Y + 2.0f, eye.Z); // Random offset :)

        var near = context.RenderView.NearClipPlane;
        var far = context.RenderView.FarClipPlane;

        var projection = Matrix.PerspectiveFovRH(MathUtil.DegreesToRadians(90.0f), 1.0f, near, far);

        context.RenderSystem.Views.Add(_renderView);

        var view = (CubeMapFace)_currentFace switch
        {
            CubeMapFace.PositiveX => Matrix.LookAtRH(cameraPosition, cameraPosition + Vector3.UnitX, Vector3.UnitY),
            CubeMapFace.NegativeX => Matrix.LookAtRH(cameraPosition, cameraPosition - Vector3.UnitX, Vector3.UnitY),
            CubeMapFace.PositiveY => Matrix.LookAtRH(cameraPosition, cameraPosition + Vector3.UnitY, Vector3.UnitZ),
            CubeMapFace.NegativeY => Matrix.LookAtRH(cameraPosition, cameraPosition - Vector3.UnitY, -Vector3.UnitZ),
            CubeMapFace.PositiveZ => Matrix.LookAtRH(cameraPosition, cameraPosition - Vector3.UnitZ, Vector3.UnitY),
            CubeMapFace.NegativeZ => Matrix.LookAtRH(cameraPosition, cameraPosition + Vector3.UnitZ, Vector3.UnitY),
            _ => throw new ArgumentOutOfRangeException(),
        };

        var viewProjection = view * projection;

        _renderView.ViewSize = new(Resolution, Resolution);
        _renderView.View = view;
        _renderView.Projection = projection;
        _renderView.NearClipPlane = near;
        _renderView.FarClipPlane = far;
        _renderView.Frustum = new(ref viewProjection);

        Matrix.Multiply(ref _renderView.View, ref _renderView.Projection, out _renderView.ViewProjection);

        // Assume culling won't be needed. 
        _renderView.CullingMode = CameraCullingMode.None;

        using (context.PushRenderViewAndRestore(_renderView))
        using (context.PushTagAndRestore(IsRenderingCubemap, true))
        using (context.SaveRenderOutputAndRestore())
        using (context.SaveViewportAndRestore())
        using (context.PushTagAndRestore(CameraComponentRendererExtensions.Current, null))
        {
            _renderView.CullingMask = RenderMask;

            context.RenderOutput.RenderTargetFormat0 = PixelFormat.R16G16B16A16_Float;
            context.RenderOutput.RenderTargetCount = 1;
            context.ViewportState.Viewport0 = new Viewport(0, 0, Resolution, Resolution);

            Child?.Collect(context);
        }
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        using (context.PushRenderViewAndRestore(_renderView))
        using (context.PushTagAndRestore(IsRenderingCubemap, true))
        using (context.PushTagAndRestore(CameraComponentRendererExtensions.Current, null))
        using (drawContext.PushRenderTargetsAndRestore())
        {
            using (drawContext.QueryManager.BeginProfile(Color4.Black, RenderCubeMapProfilingKey))
            {
                _renderView.CullingMask = RenderMask;

                var depthBuffer = PushScopedResource(context.Allocator.GetTemporaryTexture2D(Resolution, Resolution, drawContext.CommandList.DepthStencilBuffer.ViewFormat, TextureFlags.DepthStencil | TextureFlags.ShaderResource));
                var renderTexture = PushScopedResource(context.Allocator.GetTemporaryTexture2D(Resolution, Resolution, PixelFormat.R16G16B16A16_Float));

                drawContext.CommandList.SetRenderTargetAndViewport(depthBuffer, renderTexture);

                Child?.Draw(drawContext);

                drawContext.CommandList.CopyRegion(renderTexture, 0, null, _cubeMap, _currentFace);
            }
        }

        if (Skybox != null && _specularRadiancePrefilterGGX != null && _lamberFiltering != null)
        {
            using (drawContext.QueryManager.BeginProfile(Color4.Black, SpecularProbeProfilingKey))
            {
                using (drawContext.PushRenderTargetsAndRestore())
                {
                    _specularRadiancePrefilterGGX.Draw(drawContext);
                    drawContext.CommandList.ClearState();
                }

                Skybox.SpecularLightingParameters.Set(SkyboxKeys.CubeMap, _cubeMapSpecular);
            }
        }
    }
}
