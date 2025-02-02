using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using Stride.Rendering.ComputeEffect.GGXPrefiltering;
using Stride.Rendering.Images;
using Stride.Rendering.Skyboxes;
using StrideTerrain.Rendering;
using StrideTerrain.Weather.Effects.Atmosphere;
using StrideTerrain.Weather.Effects.Atmosphere.LUT;
using StrideTerrain.Weather.Effects.Fog;
using System;

namespace StrideTerrain.Weather;

public class WeatherRenderFeature : RootRenderFeature
{
    [DataMember] public RenderStage? Opaque { get; set; }
    [DataMember] public RenderStage? Transparent { get; set; }

    public override Type SupportedRenderObjectType => typeof(WeatherRenderObject);

    private LookupTextureEffect? _transmittanceLut;
    private LookupTextureEffect? _mulitScatteredLuminanceLut;
    private LookupTextureEffect? _skyLuminanceLut;
    private LookupTextureEffect? _skyViewLut;
    private LookupTextureEffect? _cameraVolumeLut;

    private ImageEffectShader? _renderSkyEffect;
    private ImageEffectShader? _renderSkyEffectNoSun;
    private ImageEffectShader? _renderFogEffect;
    private ComputeEffectShader? _renderAerialPerspectiveEffect;

    private Texture? _depthShaderResourceView;

    private SpriteBatch? _spriteBatch;

    public WeatherRenderObject? ActiveWeatherRenderObject { get; private set; }
    public Texture? TransmittanceLut => _transmittanceLut?.Texture;
    public Texture? SkyLuminanceLut => _skyLuminanceLut?.Texture;
    public Texture? CameraVolumeLut => _cameraVolumeLut?.Texture;

    private RadiancePrefilteringGGX? _specularRadiancePrefilterGGX;
    private Texture? _cubeMapSpecular;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        SortKey = 0; // Should always draw first

        // Setup lut effects
        _transmittanceLut = new(this, Context, "AtmosphereTransmittanceLut", 256, 64, 1, PixelFormat.R16G16B16A16_Float);
        _mulitScatteredLuminanceLut = new(this, Context, "AtmosphereMultiScatteredLuminanceLut", 32, 32, 1, PixelFormat.R16G16B16A16_Float);
        _skyLuminanceLut = new(this, Context, "AtmosphereSkyLuminanceLut", 1, 1, 1, PixelFormat.R16G16B16A16_Float);
        _skyViewLut = new(this, Context, "AtmosphereSkyViewLut", 192, 104, 1, PixelFormat.R16G16B16A16_Float);
        _cameraVolumeLut = new(this, Context, "AtmosphereCameraVolumeLut", 32, 32, 32, PixelFormat.R16G16B16A16_Float);

        // Render effects
        _renderSkyEffect = new("AtmosphereRenderSkyEffect");
        _renderSkyEffect.DisposeBy(this);
        _renderSkyEffect.Parameters.Set(AtmosphereEffectParameters.RenderSun, true);
        _renderSkyEffect.DepthStencilState = new DepthStencilStateDescription(true, false)
        {
            DepthBufferFunction = CompareFunction.Equal
        };

        _renderSkyEffectNoSun = new("AtmosphereRenderSkyEffect");
        _renderSkyEffectNoSun.DisposeBy(this);
        _renderSkyEffectNoSun.Parameters.Set(AtmosphereEffectParameters.RenderSun, false);
        _renderSkyEffectNoSun.DepthStencilState = new DepthStencilStateDescription(true, false)
        {
            DepthBufferFunction = CompareFunction.Equal
        };

        _renderFogEffect = new("FogRenderFog");
        _renderFogEffect.DisposeBy(this);
        _renderFogEffect.DepthStencilState = new DepthStencilStateDescription(true, false)
        {
            DepthBufferFunction = CompareFunction.Less
        };
        _renderFogEffect.BlendState = BlendStates.AlphaBlend;

        _renderAerialPerspectiveEffect = new(Context) { ShaderSourceName = "AtmosphereRenderAerialPerspective" };
        _renderAerialPerspectiveEffect.DisposeBy(this);

        _spriteBatch = new(Context.GraphicsDevice);
        _spriteBatch.DisposeBy(this);
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        base.Draw(context, renderView, renderViewStage, startIndex, endIndex);

        ActiveWeatherRenderObject = null;

        // Can only ever be one weather component active so abort if empty
        if (startIndex == endIndex)
            return;

        var renderNodeReference = renderViewStage.SortedRenderNodes[startIndex].RenderNode;
        var renderNode = GetRenderNode(renderNodeReference);
        var renderObject = (WeatherRenderObject)renderNode.RenderObject;
        ActiveWeatherRenderObject = renderObject;

        var atmosphere = renderObject.Atmosphere;
        var fog = renderObject.Fog;
        var sunDirection = renderObject.SunDirection;
        var sunColor = renderObject.SunColor;

        var inverseViewMatrix = Matrix.Invert(renderView.View);
        var eye = inverseViewMatrix.Row4;
        var cameraPosition = new Vector3(eye.X, eye.Y, eye.Z);

        var invViewProjection = Matrix.Invert(renderView.ViewProjection);

        var invViewSize = 1.0f / renderView.ViewSize;

        if (renderViewStage.Index == Opaque?.Index)
        {
            // Render all LUT textures.
            RenderTransmittanceLut(context, atmosphere, sunDirection, sunColor);
            RenderMulitScatteredLuminanceLut(context, atmosphere);
            RenderSkyLuminanceLut(context, atmosphere, sunDirection, sunColor);
            RenderSkyViewLut(context, atmosphere, cameraPosition, sunDirection, sunColor);
            RenderCameraVolume(context, atmosphere, invViewProjection, cameraPosition, sunDirection, sunColor);

            // TODO: This should not really be done here ... but will do for now!
            // Would make more sense to have it in the cubemap renderer but a bit annoying to link stuff.
            if (context.RenderContext.Tags.TryGetValue(CubeMapRenderer.Cubemap, out var cubeMap) && cubeMap != null && renderObject.SkyBoxSpecularLightingParameters != null)
            {
                if (_specularRadiancePrefilterGGX == null)
                {
                    _specularRadiancePrefilterGGX = new(context.RenderContext);
                    _specularRadiancePrefilterGGX.DisposeBy(this);
                }

                if (_cubeMapSpecular == null || _cubeMapSpecular.Width != cubeMap.Width)
                {
                    _cubeMapSpecular?.RemoveDisposeBy(this);
                    _cubeMapSpecular?.Dispose();

                    _cubeMapSpecular = Texture.NewCube(Context.GraphicsDevice, cubeMap.Width, MipMapCount.Auto, PixelFormat.R16G16B16A16_Float, TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);
                    _cubeMapSpecular.DisposeBy(this);
                }

                _specularRadiancePrefilterGGX.RadianceMap = cubeMap;
                _specularRadiancePrefilterGGX.PrefilteredRadiance = _cubeMapSpecular;
                _specularRadiancePrefilterGGX.SamplingsCount = 16;

                using (context.PushRenderTargetsAndRestore())
                {
                    _specularRadiancePrefilterGGX.Draw(context);
                    context.CommandList.ClearState();
                }

                renderObject.SkyBoxSpecularLightingParameters.Set(SkyboxKeys.CubeMap, _cubeMapSpecular);
            }
        }
        else if (renderViewStage.Index == Transparent?.Index)
        {
            // We only draw the weather effects in the transparent render stage as a post effect over the opaque stage,
            // this also gives us easy access to the depth buffer.

            // Might be useful to use this in the future if more effects that read the aerial perspective are added (like clouds maybe?), in that case the forward effect should be modified to not sample the aerial perspective.
            //var aerialPerspectiveRenderTarget = context.RenderContext.Allocator.GetTemporaryTexture2D((int)viewSize.X, (int)viewSize.Y, PixelFormat.R16G16B16A16_Float, TextureFlags.UnorderedAccess | TextureFlags.ShaderResource); ;
            //RenderAerialPerspectiveTexture(context, atmosphere, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize, viewSize, aerialPerspectiveRenderTarget);

            RenderSky(context, atmosphere, fog, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize);
            //RenderAerialPerspective(context, aerialPerspectiveRenderTarget);
            //RenderFog(context, atmosphere, fog, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize);

            _depthShaderResourceView = null;
            //context.RenderContext.Allocator.ReleaseReference(aerialPerspectiveRenderTarget);
        }
    }

    #region Sky & Fog
    private void RenderAerialPerspective(RenderDrawContext context, Texture aerialPerspectiveRenderTarget)
    {
        if (_spriteBatch == null)
            return;

        var blendState = BlendStates.AlphaBlend;
        var depthState = DepthStencilStates.None;

        _spriteBatch.Begin(context.GraphicsContext, SpriteSortMode.Immediate, blendState, depthStencilState: depthState);
        _spriteBatch.Draw(aerialPerspectiveRenderTarget, Vector2.Zero);
        _spriteBatch.End();
    }

    private void RenderAerialPerspectiveTexture(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 sunDirection, Color3 sunColor, Vector3 cameraPosition, Matrix invViewProjection, Vector2 invViewSize, Vector2 viewSize, Texture aerialPerspectiveRenderTarget)
    {
        if (_depthShaderResourceView == null || _renderAerialPerspectiveEffect == null || _transmittanceLut == null || _mulitScatteredLuminanceLut == null || _cameraVolumeLut == null)
            return;

        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.MultiScatteringLUT, _mulitScatteredLuminanceLut.Texture);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.CameraVolumeLUT, _cameraVolumeLut.Texture);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.Depth, _depthShaderResourceView);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.OutputTexture, aerialPerspectiveRenderTarget);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.Atmosphere, atmosphere);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.SunDirection, sunDirection);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.SunColor, sunColor);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.CameraPosition, cameraPosition);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.InvViewProjection, invViewProjection);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.InvResolution, invViewSize);

        _renderAerialPerspectiveEffect.ThreadNumbers = new(8, 8, 1);
        _renderAerialPerspectiveEffect.ThreadGroupCounts = new((int)Math.Ceiling(aerialPerspectiveRenderTarget.Width / 8.0f), (int)Math.Ceiling(aerialPerspectiveRenderTarget.Height / 8.0f), 1);
        _renderAerialPerspectiveEffect.Draw(context);
    }

    private void RenderSky(RenderDrawContext context, AtmosphereParameters atmosphere, FogParameters fog, Vector3 sunDirection, Color3 sunColor, Vector3 cameraPosition, Matrix invViewProjection, Vector2 invViewSize)
    {
        if (_depthShaderResourceView == null || _renderSkyEffect == null || _transmittanceLut == null || _mulitScatteredLuminanceLut == null || _skyViewLut == null || _skyLuminanceLut == null)
            return;

        context.RenderContext.Tags.TryGetValue(CubeMapRenderer.IsRenderingCubemap, out var isRenderingCubeMap);

        var renderSkyEffect = isRenderingCubeMap ? _renderSkyEffectNoSun : _renderSkyEffect;
        if (renderSkyEffect == null)
            return;

        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.TransmittanceLUT, _transmittanceLut.Texture);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.MultiScatteringLUT, _mulitScatteredLuminanceLut.Texture);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SkyViewLUT, _skyViewLut.Texture);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SkyLuminanceLUT, _skyLuminanceLut.Texture);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.Atmosphere, atmosphere);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.Fog, fog);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SunDirection, sunDirection);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SunColor, sunColor);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.CameraPosition, cameraPosition);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.InvViewProjection, invViewProjection);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.InvResolution, invViewSize);
        renderSkyEffect!.Draw(context, "Atmosphere.RenderSky");
    }

    private void RenderFog(RenderDrawContext context, AtmosphereParameters atmosphere, FogParameters fog, Vector3 sunDirection, Color3 sunColor, Vector3 cameraPosition, Matrix invViewProjection, Vector2 invViewSize)
    {
        if (_depthShaderResourceView == null || _renderFogEffect == null || _transmittanceLut == null || _skyLuminanceLut == null)
            return;

        _renderFogEffect.Parameters.Set(FogRenderFogKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.SkyLuminanceLUT, _skyLuminanceLut.Texture);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.DepthTexture, _depthShaderResourceView);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.Atmosphere, atmosphere);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.Fog, fog);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.SunDirection, sunDirection);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.SunColor, sunColor);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.CameraPosition, cameraPosition);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.InvViewProjection, invViewProjection);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.InvResolution, invViewSize);
        _renderFogEffect!.Draw(context, "Atmosphere.RenderFog");
    }
    #endregion

    #region Render Atmosphere LUT
    void RenderTransmittanceLut(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 sunDirection, Color3 sunColor)
    {
        if (_transmittanceLut == null)
            return;

        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.OutputTexture, _transmittanceLut.Texture);
        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.Atmosphere, atmosphere);
        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.SunDirection, sunDirection);
        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.SunColor, sunColor);

        _transmittanceLut.Effect.ThreadNumbers = new(8, 8, 1);
        _transmittanceLut.Effect.ThreadGroupCounts = new(_transmittanceLut.Texture.Width / 8, _transmittanceLut.Texture.Height / 8, 1);
        _transmittanceLut.Effect.Draw(context, "Atmosphere.LUT.Transmittance");
    }

    void RenderMulitScatteredLuminanceLut(RenderDrawContext context, AtmosphereParameters atmosphere)
    {
        if (_transmittanceLut == null || _mulitScatteredLuminanceLut == null)
            return;

        _mulitScatteredLuminanceLut.Effect.Parameters.Set(AtmosphereMultiScatteredLuminanceLutKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _mulitScatteredLuminanceLut.Effect.Parameters.Set(AtmosphereMultiScatteredLuminanceLutKeys.OutputTexture, _mulitScatteredLuminanceLut.Texture);
        _mulitScatteredLuminanceLut.Effect.Parameters.Set(AtmosphereMultiScatteredLuminanceLutKeys.Atmosphere, atmosphere);

        _mulitScatteredLuminanceLut.Effect.ThreadNumbers = new(1, 1, 64);
        _mulitScatteredLuminanceLut.Effect.ThreadGroupCounts = new(_mulitScatteredLuminanceLut.Texture.Width, _mulitScatteredLuminanceLut.Texture.Height, 1);
        _mulitScatteredLuminanceLut.Effect.Draw(context, "Atmosphere.LUT.MultiScatteredLuminance");
    }

    void RenderSkyLuminanceLut(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 sunDirection, Color3 sunColor)
    {
        if (_transmittanceLut == null || _mulitScatteredLuminanceLut == null || _skyLuminanceLut == null)
            return;

        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.MultiScatteringLUT, _mulitScatteredLuminanceLut.Texture);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.OutputTexture, _skyLuminanceLut.Texture);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.Atmosphere, atmosphere);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.SunDirection, sunDirection);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.SunColor, sunColor);

        _skyLuminanceLut.Effect.ThreadNumbers = new(1, 1, 64);
        _skyLuminanceLut.Effect.ThreadGroupCounts = new(_skyLuminanceLut.Texture.Width, _skyLuminanceLut.Texture.Height, 1);
        _skyLuminanceLut.Effect.Draw(context, "Atmosphere.LUT.SkyLuminance");
    }

    void RenderSkyViewLut(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 cameraPosition, Vector3 sunDirection, Color3 sunColor)
    {
        if (_transmittanceLut == null || _mulitScatteredLuminanceLut == null || _skyViewLut == null)
            return;

        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.MultiScatteringLUT, _mulitScatteredLuminanceLut.Texture);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.OutputTexture, _skyViewLut.Texture);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.Atmosphere, atmosphere);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.SunDirection, sunDirection);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.SunColor, sunColor);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.CameraPosition, cameraPosition);

        _skyViewLut.Effect.ThreadNumbers = new(8, 8, 1);
        _skyViewLut.Effect.ThreadGroupCounts = new(_skyViewLut.Texture.Width / 8, _skyViewLut.Texture.Height / 8, 1);
        _skyViewLut.Effect.Draw(context, "Atmosphere.LUT.SkyViewLut");
    }

    void RenderCameraVolume(RenderDrawContext context, AtmosphereParameters atmosphere, Matrix invViewProjection, Vector3 cameraPosition, Vector3 sunDirection, Color3 sunColor)
    {
        if (_transmittanceLut == null || _mulitScatteredLuminanceLut == null || _cameraVolumeLut == null)
            return;

        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.MultiScatteringLUT, _mulitScatteredLuminanceLut.Texture);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.OutputTexture, _cameraVolumeLut.Texture);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.Atmosphere, atmosphere);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.SunDirection, sunDirection);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.SunColor, sunColor);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.CameraPosition, cameraPosition);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.InvViewProjection, invViewProjection);

        _cameraVolumeLut.Effect.ThreadNumbers = new(8, 8, 8);
        _cameraVolumeLut.Effect.ThreadGroupCounts = new(_cameraVolumeLut.Texture.Width / 8, _cameraVolumeLut.Texture.Height / 8, _cameraVolumeLut.Texture.Depth / 8);
        _cameraVolumeLut.Effect.Draw(context, "Atmosphere.LUT.CameraVolume");
    }
    #endregion

    public override void BindPerViewShaderResource(string logicalGroupName, RenderView renderView, GraphicsResource resource)
    {
        base.BindPerViewShaderResource(logicalGroupName, renderView, resource);

        if (logicalGroupName == "Depth")
        {
            _depthShaderResourceView = (Texture)resource;
        }
    }
}
