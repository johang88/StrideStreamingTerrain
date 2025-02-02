﻿using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using System;

namespace StrideTerrain.Rendering;
public class CubeMapRenderer : SceneRendererBase
{
    public static readonly PropertyKey<bool> IsRenderingCubemap = new("CubeMapRenderer.IsRenderingCubemap", typeof(RenderContext));
    public static readonly PropertyKey<Texture?> Cubemap = new("CubeMapRenderer.CubeMap", typeof(RenderContext));

    private RenderView _renderView = new();

    public ISceneRenderer? Child { get; set; }
    public RenderGroupMask RenderMask { get; set; } = RenderGroupMask.All;
    public int Resolution { get; set; } = 1024;

    private int _currentFace = 0; // Render one face per frame

    private Texture? _cubeMap = null;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _cubeMap = Texture.NewCube(Context.GraphicsDevice, Resolution, PixelFormat.R16G16B16A16_Float, TextureFlags.ShaderResource);
        _cubeMap.DisposeBy(this);
    }

    protected override void CollectCore(RenderContext context)
    {
        base.CollectCore(context);

        context.Tags.Set(Cubemap, _cubeMap);

        _currentFace++;
        if (_currentFace >= 6)
        {
            _currentFace = 0;
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
        // TODO: Set render target!
        using (context.PushRenderViewAndRestore(_renderView))
        using (context.PushTagAndRestore(IsRenderingCubemap, true))
        using (context.PushTagAndRestore(CameraComponentRendererExtensions.Current, null))
        using (drawContext.PushRenderTargetsAndRestore())
        {
            _renderView.CullingMask = RenderMask;

            var depthBuffer = PushScopedResource(context.Allocator.GetTemporaryTexture2D(Resolution, Resolution, drawContext.CommandList.DepthStencilBuffer.ViewFormat, TextureFlags.DepthStencil | TextureFlags.ShaderResource));
            var renderTexture = PushScopedResource(context.Allocator.GetTemporaryTexture2D(Resolution, Resolution, PixelFormat.R16G16B16A16_Float));

            drawContext.CommandList.SetRenderTargetAndViewport(depthBuffer, renderTexture);

            Child?.Draw(drawContext);

            // TODO: Copy to correct face
            drawContext.CommandList.CopyRegion(renderTexture, 0, null, _cubeMap, _currentFace);
        }
    }
}
