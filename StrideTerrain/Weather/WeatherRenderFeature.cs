using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Stride.Shaders;
using StrideTerrain.Rendering;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Rendering;
using StrideTerrain.Weather.Effects.Atmosphere;
using StrideTerrain.Weather.Effects.Atmosphere.LUT;
using StrideTerrain.Weather.Effects.Fog;
using StrideTerrain.Weather.Effects.Lights;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrideTerrain.Weather;

public class WeatherRenderFeature : RootRenderFeature
{
    public static readonly PropertyKey<Texture> CameraVolumeLut = new("WeatherRenderFeature.CameraVolumeLut", typeof(WeatherRenderObject));

    [DataMember] public RenderStage? Opaque { get; set; }
    [DataMember] public RenderStage? Transparent { get; set; }

    public override Type SupportedRenderObjectType => typeof(WeatherRenderObject);

    private LookupTextureEffect? _cameraVolumeLut;

    private ImageEffectShader? _renderSkyEffect;
    private ImageEffectShader? _renderSkyEffectNoSun;
    private ImageEffectShader? _renderFogEffect;
    private ImageEffectShader? _renderVolumetricLightDirectional;
    private ComputeEffectShader? _renderAerialPerspectiveEffect;

    private Texture? _depthShaderResourceView;

    private SpriteBatch? _spriteBatch;

    private ILightShadowMapRenderer? _shadowMapRenderer;
    private LightShadowType? _shadowType;
    private LightGroupRendererDynamic? _groupRenderer;
    private LightShaderGroupDynamic? _shaderGroup;
    private List<RenderView> _renderViews = [];

    // Volumetric cloud state
    private ComputeEffectShader? _cloudTraceEffect;
    private ComputeEffectShader? _cloudReconstructEffect;
    private Texture? _cloudTraceBuffer;        // raw sparse trace output
    private Texture? _cloudReconstructA;       // reconstruction ping-pong A
    private Texture? _cloudReconstructB;       // reconstruction ping-pong B
    private bool _cloudPingPong;
    private uint _frameIndex;
    private Matrix _prevViewProjection;

    // Precomputed noise textures (generated once at init)
    private ComputeEffectShader? _basicNoiseEffect;
    private ComputeEffectShader? _detailNoiseEffect;
    private Texture? _basicNoiseTexture;
    private Texture? _detailNoiseTexture;
    private bool _noiseTexturesGenerated;

    // Weather map (regenerated when parameters change)
    private ComputeEffectShader? _weatherMapEffect;
    private Texture? _weatherMapTexture;
    private int _weatherMapSize;
    private float _lastCoverage = -1;
    private float _lastCoverageScale = -1;
    private float _lastTypeScale = -1;

    private const int BasicNoiseSize = 128;
    private const int DetailNoiseSize = 32;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        SortKey = 0; // Should always draw first

        _cameraVolumeLut = new(this, Context, "AtmosphereCameraVolumeLut", 32, 32, 32, PixelFormat.R16G16B16A16_Float);

        _renderSkyEffect = new("AtmosphereRenderSkyEffect");
        _renderSkyEffect.DisposeBy(this);
        _renderSkyEffect.Parameters.Set(AtmosphereEffectParameters.RenderSun, true);
        _renderSkyEffect.Parameters.Set(AtmosphereEffectParameters.RenderClouds, true);
        _renderSkyEffect.DepthStencilState = new DepthStencilStateDescription(true, false)
        {
            DepthBufferFunction = CompareFunction.Equal
        };

        _renderSkyEffectNoSun = new("AtmosphereRenderSkyEffect");
        _renderSkyEffectNoSun.DisposeBy(this);
        _renderSkyEffectNoSun.Parameters.Set(AtmosphereEffectParameters.RenderSun, false);
        _renderSkyEffectNoSun.Parameters.Set(AtmosphereEffectParameters.RenderClouds, false);
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

        _renderVolumetricLightDirectional = new ImageEffectShader("VolumetricLightDiretionalEffect");
        _renderVolumetricLightDirectional.Initialize(Context);
        _renderVolumetricLightDirectional.DisposeBy(this);
        _renderVolumetricLightDirectional.BlendState = BlendStates.AlphaBlend;

        _renderAerialPerspectiveEffect = new(Context) { ShaderSourceName = "AtmosphereRenderAerialPerspective" };
        _renderAerialPerspectiveEffect.DisposeBy(this);

        _cloudTraceEffect = new(Context) { ShaderSourceName = "VolumetricCloudsTrace" };
        _cloudTraceEffect.DisposeBy(this);

        _cloudReconstructEffect = new(Context) { ShaderSourceName = "CloudReconstruct" };
        _cloudReconstructEffect.DisposeBy(this);

        // Noise generation compute shaders
        _basicNoiseEffect = new(Context) { ShaderSourceName = "CloudBasicNoise" };
        _basicNoiseEffect.DisposeBy(this);

        _detailNoiseEffect = new(Context) { ShaderSourceName = "CloudDetailNoise" };
        _detailNoiseEffect.DisposeBy(this);

        _weatherMapEffect = new(Context) { ShaderSourceName = "CloudWeatherMap" };
        _weatherMapEffect.DisposeBy(this);

        _spriteBatch = new(Context.GraphicsDevice);
        _spriteBatch.DisposeBy(this);
    }

    protected override void Destroy()
    {
        _cloudTraceBuffer?.Dispose();
        _cloudReconstructA?.Dispose();
        _cloudReconstructB?.Dispose();
        _cloudTraceBuffer = null;
        _cloudReconstructA = null;
        _cloudReconstructB = null;

        _basicNoiseTexture?.Dispose();
        _detailNoiseTexture?.Dispose();
        _basicNoiseTexture = null;
        _detailNoiseTexture = null;

        _weatherMapTexture?.Dispose();
        _weatherMapTexture = null;

        base.Destroy();
    }

    public override void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        if (!context.RenderContext.Tags.TryGetValue(WeatherRenderObject.Current, out var weather) || _cameraVolumeLut == null)
        {
            context.RenderContext.Tags.Remove(CameraVolumeLut);
        }
        else
        {
            context.RenderContext.Tags.Set(CameraVolumeLut, _cameraVolumeLut.Texture);
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        base.Draw(context, renderView, renderViewStage, startIndex, endIndex);

        // Can only ever be one weather component active so abort if empty
        if (startIndex == endIndex)
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.TransmittanceLut, out var transmittanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.MultiScatteredLuminanceLut, out var multiScatteredLuminanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.SkyLuminanceLut, out var skyLuminanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.SkyViewLut, out var skyViewLut))
            return;

        var renderNodeReference = renderViewStage.SortedRenderNodes[startIndex].RenderNode;
        var renderNode = GetRenderNode(renderNodeReference);
        var renderObject = (WeatherRenderObject)renderNode.RenderObject;

        var atmosphere = renderObject.Atmosphere;
        var fog = renderObject.Fog;
        var clouds = renderObject.Clouds;
        var weatherMap = renderObject.WeatherMap;
        var sunDirection = renderObject.SunDirection;
        var sunColor = renderObject.SunColor;

        var inverseViewMatrix = Matrix.Invert(renderView.View);
        var eye = inverseViewMatrix.Row4;
        var cameraPosition = new Vector3(eye.X, eye.Y, eye.Z);

        var invViewProjection = Matrix.Invert(renderView.ViewProjection);

        var invViewSize = 1.0f / renderView.ViewSize;

        if (renderViewStage.Index == Opaque?.Index)
        {
            RenderCameraVolume(context, atmosphere, invViewProjection, cameraPosition, sunDirection, sunColor, transmittanceLut, multiScatteredLuminanceLut);
        }
        else if (renderViewStage.Index == Transparent?.Index)
        {
            // Ensure noise textures are generated (one-time)
            EnsureNoiseTextures(context);

            // Update weather map if parameters changed
            UpdateWeatherMap(context, clouds, weatherMap);

            // Dispatch volumetric cloud compute pass before sky rendering
            Texture? cloudBuffer = null;

            context.RenderContext.Tags.TryGetValue(CubeMapRenderer.IsRenderingCubemap, out var isRenderingCubeMap);
            if (!isRenderingCubeMap)
            {
                cloudBuffer = RenderVolumetricClouds(context, clouds, atmosphere, sunDirection, sunColor, cameraPosition,
                    invViewProjection, renderView.ViewProjection, renderView.ViewSize, transmittanceLut, skyLuminanceLut);
            }

            RenderSky(context, atmosphere, fog, clouds, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize, transmittanceLut, multiScatteredLuminanceLut, skyLuminanceLut, skyViewLut, cloudBuffer);

            _depthShaderResourceView = null;
        }
    }

    #region Noise Texture Generation
    private void EnsureNoiseTextures(RenderDrawContext context)
    {
        if (_noiseTexturesGenerated)
            return;

        if (_basicNoiseEffect == null || _detailNoiseEffect == null)
            return;

        var device = context.GraphicsDevice;

        // Create 3D textures
        _basicNoiseTexture = Texture.New3D(device, BasicNoiseSize, BasicNoiseSize, BasicNoiseSize,
            PixelFormat.R16G16B16A16_Float, TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);

        _detailNoiseTexture = Texture.New3D(device, DetailNoiseSize, DetailNoiseSize, DetailNoiseSize,
            PixelFormat.R16G16B16A16_Float, TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);

        // Generate basic noise (128^3)
        _basicNoiseEffect.Parameters.Set(CloudBasicNoiseKeys.OutputTexture, _basicNoiseTexture);
        _basicNoiseEffect.Parameters.Set(CloudBasicNoiseKeys.TextureSize, (uint)BasicNoiseSize);
        _basicNoiseEffect.ThreadNumbers = new Int3(8, 8, 8);
        _basicNoiseEffect.ThreadGroupCounts = new Int3(
            BasicNoiseSize / 8, BasicNoiseSize / 8, BasicNoiseSize / 8);
        _basicNoiseEffect.Draw(context, "Clouds.GenerateBasicNoise");

        // Generate detail noise (32^3)
        _detailNoiseEffect.Parameters.Set(CloudDetailNoiseKeys.OutputTexture, _detailNoiseTexture);
        _detailNoiseEffect.Parameters.Set(CloudDetailNoiseKeys.TextureSize, (uint)DetailNoiseSize);
        _detailNoiseEffect.ThreadNumbers = new Int3(8, 8, 8);
        _detailNoiseEffect.ThreadGroupCounts = new Int3(
            DetailNoiseSize / 8, DetailNoiseSize / 8, DetailNoiseSize / 8);
        _detailNoiseEffect.Draw(context, "Clouds.GenerateDetailNoise");

        _noiseTexturesGenerated = true;
    }

    private void UpdateWeatherMap(RenderDrawContext context, CloudParameters clouds, WeatherMapParameters weatherMap)
    {
        if (_weatherMapEffect == null)
            return;

        var mapSize = weatherMap.MapSize;

        // Check if we need to regenerate
        // Coverage is no longer baked into the map, but we track it to force
        // shader recompilation when the user changes parameters in the editor.
        bool needsRegenerate = _weatherMapTexture == null
            || _weatherMapSize != mapSize
            || Math.Abs(_lastCoverage - clouds.Coverage) > 0.001f
            || Math.Abs(_lastCoverageScale - weatherMap.CoverageScale) > 0.001f
            || Math.Abs(_lastTypeScale - weatherMap.TypeScale) > 0.001f;

        if (!needsRegenerate)
            return;

        // Create/recreate texture if size changed
        if (_weatherMapTexture == null || _weatherMapSize != mapSize)
        {
            _weatherMapTexture?.Dispose();
            _weatherMapTexture = Texture.New2D(context.GraphicsDevice, mapSize, mapSize,
                PixelFormat.R16G16B16A16_Float, TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);
            _weatherMapSize = mapSize;
        }

        _weatherMapEffect.Parameters.Set(CloudWeatherMapKeys.OutputTexture, _weatherMapTexture);
        _weatherMapEffect.Parameters.Set(CloudWeatherMapKeys.MapSize, (uint)mapSize);
        _weatherMapEffect.Parameters.Set(CloudWeatherMapKeys.Coverage, clouds.Coverage);
        _weatherMapEffect.Parameters.Set(CloudWeatherMapKeys.CoverageScale, weatherMap.CoverageScale);
        _weatherMapEffect.Parameters.Set(CloudWeatherMapKeys.TypeScale, weatherMap.TypeScale);

        _weatherMapEffect.ThreadNumbers = new Int3(8, 8, 1);
        _weatherMapEffect.ThreadGroupCounts = new Int3(
            (int)Math.Ceiling(mapSize / 8.0),
            (int)Math.Ceiling(mapSize / 8.0), 1);
        _weatherMapEffect.Draw(context, "Clouds.GenerateWeatherMap");

        _lastCoverage = clouds.Coverage;
        _lastCoverageScale = weatherMap.CoverageScale;
        _lastTypeScale = weatherMap.TypeScale;
    }
    #endregion

    #region Volumetric Clouds
    private void EnsureCloudBuffers(GraphicsDevice device, int fullWidth, int fullHeight)
    {
        // Trace buffer is quarter-resolution
        var traceW = Math.Max(1, fullWidth / 4);
        var traceH = Math.Max(1, fullHeight / 4);

        if (_cloudTraceBuffer != null && _cloudTraceBuffer.Width == traceW && _cloudTraceBuffer.Height == traceH
            && _cloudReconstructA != null && _cloudReconstructA.Width == fullWidth && _cloudReconstructA.Height == fullHeight)
            return;

        _cloudTraceBuffer?.Dispose();
        _cloudReconstructA?.Dispose();
        _cloudReconstructB?.Dispose();

        // Quarter-res trace buffer
        _cloudTraceBuffer = Texture.New2D(device, traceW, traceH, PixelFormat.R16G16B16A16_Float,
            TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);

        // Full-res reconstruction ping-pong buffers
        _cloudReconstructA = Texture.New2D(device, fullWidth, fullHeight, PixelFormat.R16G16B16A16_Float,
            TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);
        _cloudReconstructB = Texture.New2D(device, fullWidth, fullHeight, PixelFormat.R16G16B16A16_Float,
            TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);

        // Reset temporal state on resize
        _frameIndex = 0;
        _cloudPingPong = false;
    }

    private Texture? RenderVolumetricClouds(RenderDrawContext context, CloudParameters clouds, AtmosphereParameters atmosphere,
        Vector3 sunDirection, Color3 sunColor, Vector3 cameraPosition,
        Matrix invViewProjection, Matrix viewProjection, Vector2 viewSize,
        Texture transmittanceLut, Texture skyLuminanceLut)
    {
        if (_cloudTraceEffect == null || _cloudReconstructEffect == null
            || _basicNoiseTexture == null || _detailNoiseTexture == null || _weatherMapTexture == null)
            return null;

        var width = (int)viewSize.X;
        var height = (int)viewSize.Y;
        if (width <= 0 || height <= 0)
            return null;

        EnsureCloudBuffers(context.GraphicsDevice, width, height);

        if (_cloudTraceBuffer == null || _cloudReconstructA == null || _cloudReconstructB == null)
            return null;

        var reconstructWrite = _cloudPingPong ? _cloudReconstructB : _cloudReconstructA;
        var reconstructRead = _cloudPingPong ? _cloudReconstructA : _cloudReconstructB;

        var traceW = _cloudTraceBuffer.Width;
        var traceH = _cloudTraceBuffer.Height;
        var traceResolution = new Vector2(traceW, traceH);

        // ── Pass 1: Trace (quarter-res, ALL pixels active) ──
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.OutputTexture, _cloudTraceBuffer);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.TransmittanceLUT, transmittanceLut);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.SkyLuminanceLUT, skyLuminanceLut);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.BasicNoiseTexture, _basicNoiseTexture);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.DetailNoiseTexture, _detailNoiseTexture);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.WeatherMapTexture, _weatherMapTexture);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.Clouds, clouds);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.Atmosphere, atmosphere);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.InvViewProjection, invViewProjection);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.SunDirection, sunDirection);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.SunColor, sunColor);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.CameraPosition, cameraPosition);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.TraceResolution, traceResolution);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.FullResolution, viewSize);
        _cloudTraceEffect.Parameters.Set(VolumetricCloudsTraceKeys.FrameIndex, _frameIndex);
        _cloudTraceEffect.Parameters.Set(GlobalKeys.Time, (float)context.RenderContext.Time.Total.TotalSeconds);

        _cloudTraceEffect.ThreadNumbers = new Int3(8, 8, 1);
        _cloudTraceEffect.ThreadGroupCounts = new Int3(
            (int)Math.Ceiling(traceW / 8.0),
            (int)Math.Ceiling(traceH / 8.0), 1);
        _cloudTraceEffect.Draw(context, "Clouds.VolumetricTrace");

        // ── Pass 2: Reconstruct (full resolution) ──
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.TraceTexture, _cloudTraceBuffer);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.HistoryTexture, reconstructRead);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.OutputTexture, reconstructWrite);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.Clouds, clouds);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.Atmosphere, atmosphere);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.PrevViewProjection, _prevViewProjection);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.InvViewProjection, invViewProjection);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.CameraPosition, cameraPosition);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.FullResolution, viewSize);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.TraceResolution, traceResolution);
        _cloudReconstructEffect.Parameters.Set(CloudReconstructKeys.FrameIndex, _frameIndex);

        _cloudReconstructEffect.ThreadNumbers = new Int3(8, 8, 1);
        _cloudReconstructEffect.ThreadGroupCounts = new Int3(
            (int)Math.Ceiling(width / 8.0),
            (int)Math.Ceiling(height / 8.0), 1);
        _cloudReconstructEffect.Draw(context, "Clouds.Reconstruct");

        _cloudPingPong = !_cloudPingPong;
        _prevViewProjection = viewProjection;
        _frameIndex++;

        return reconstructWrite;
    }
    #endregion

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

    private void RenderAerialPerspectiveTexture(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 sunDirection, Color3 sunColor, Vector3 cameraPosition, Matrix invViewProjection, Vector2 invViewSize, Vector2 viewSize, Texture aerialPerspectiveRenderTarget,
        Texture transmittanceLut, Texture mulitScatteredLuminanceLut)
    {
        if (_depthShaderResourceView == null || _renderAerialPerspectiveEffect == null || _cameraVolumeLut == null)
            return;

        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.TransmittanceLUT, transmittanceLut);
        _renderAerialPerspectiveEffect.Parameters.Set(AtmosphereRenderAerialPerspectiveKeys.MultiScatteringLUT, mulitScatteredLuminanceLut);
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

    private void RenderSky(RenderDrawContext context, AtmosphereParameters atmosphere, FogParameters fog, CloudParameters clouds, Vector3 sunDirection, Color3 sunColor, Vector3 cameraPosition,
        Matrix invViewProjection, Vector2 invViewSize, Texture transmittanceLut, Texture mulitScatteredLuminanceLut, Texture skyLuminanceLut, Texture skyViewLut, Texture? cloudAccumulationTexture)
    {
        if (_depthShaderResourceView == null || _renderSkyEffect == null)
            return;

        context.RenderContext.Tags.TryGetValue(CubeMapRenderer.IsRenderingCubemap, out var isRenderingCubeMap);

        var renderSkyEffect = isRenderingCubeMap ? _renderSkyEffectNoSun : _renderSkyEffect;
        if (renderSkyEffect == null)
            return;

        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.TransmittanceLUT, transmittanceLut);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.MultiScatteringLUT, mulitScatteredLuminanceLut);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SkyViewLUT, skyViewLut);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SkyLuminanceLUT, skyLuminanceLut);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.CloudAccumulationTexture, cloudAccumulationTexture);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.Atmosphere, atmosphere);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.Fog, fog);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SunDirection, sunDirection);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.SunColor, sunColor);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.CameraPosition, cameraPosition);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.InvViewProjection, invViewProjection);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.InvResolution, invViewSize);
        renderSkyEffect.Parameters.Set(AtmosphereRenderSkyKeys.Clouds, clouds);
        renderSkyEffect.Parameters.Set(AtmosphereEffectParameters.EnableHeightFog, true);
        renderSkyEffect.Parameters.Set(GlobalKeys.Time, (float)context.RenderContext.Time.Total.TotalSeconds);
        renderSkyEffect.Draw(context, "Atmosphere.RenderSky");
    }

    private void RenderFog(RenderDrawContext context, AtmosphereParameters atmosphere, FogParameters fog, Vector3 sunDirection, Color3 sunColor, Vector3 cameraPosition,
        Matrix invViewProjection, Vector2 invViewSize, Texture transmittanceLut, Texture skyLuminanceLut)
    {
        if (_depthShaderResourceView == null || _renderFogEffect == null)
            return;

        _renderFogEffect.Parameters.Set(FogRenderFogKeys.TransmittanceLUT, transmittanceLut);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.SkyLuminanceLUT, skyLuminanceLut);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.DepthTexture, _depthShaderResourceView);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.Atmosphere, atmosphere);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.Fog, fog);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.SunDirection, sunDirection);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.SunColor, sunColor);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.CameraPosition, cameraPosition);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.InvViewProjection, invViewProjection);
        _renderFogEffect.Parameters.Set(FogRenderFogKeys.InvResolution, invViewSize);
        _renderFogEffect.Draw(context, "Atmosphere.RenderFog");
    }

    private void RenderVolumetricLightDirectional(RenderDrawContext context, AtmosphereParameters atmosphere, FogParameters fog, Vector3 sunDirection, Color3 sunColor,
        Vector3 cameraPosition, Matrix invViewProjection, Vector2 invViewSize, Texture transmittanceLut, Texture skyLuminanceLut, RenderView renderView, RenderLight? light)
    {
        if (_depthShaderResourceView == null || _renderVolumetricLightDirectional == null || light == null)
            return;

        context.Tags.TryGetValue(TerrainRenderFeature.Current, out var terrain);

        // TODO: Should draw at half res and upscale

        var meshRenderFeature = RenderSystem.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        var forwardLightingFeature = meshRenderFeature?.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault();
        if (forwardLightingFeature != null)
        {
            var shadowMapRenderer = forwardLightingFeature.ShadowMapRenderer;
            var shadowMapTexture = shadowMapRenderer.FindShadowMap(renderView.LightingView ?? renderView, light);

            if (shadowMapTexture != null && _shadowMapRenderer != null)
            {
                // Detect changed shadow map renderer or type
                if (_shadowMapRenderer != shadowMapTexture.Renderer || _shadowType != shadowMapTexture.ShadowType)
                    UpdateRenderData(context, shadowMapTexture);
            }
            else if (shadowMapTexture?.Renderer != _shadowMapRenderer || _shaderGroup == null)
            {
                UpdateRenderData(context, shadowMapTexture);
            }

            _renderViews.Clear();
            _renderViews.Add(renderView);

            _shaderGroup!.Reset();
            _shaderGroup.SetViews(_renderViews);
            _shaderGroup.AddView(0, context.RenderContext.RenderView, 1);

            _shaderGroup.AddLight(light, shadowMapTexture);
            _shaderGroup.UpdateLayout("lightGroup");

            _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalEffectKeys.LightGroup, _shaderGroup.ShaderSource);

            // Update the effect here so the layout is correct
            _renderVolumetricLightDirectional.EffectInstance.UpdateEffect(RenderSystem.GraphicsDevice);

            _shaderGroup.ApplyViewParameters(context, 0, _renderVolumetricLightDirectional.Parameters);

            var box = new BoundingBoxExt(new Vector3(-float.MaxValue), new Vector3(float.MaxValue));
            _shaderGroup.ApplyDrawParameters(context, 0, _renderVolumetricLightDirectional.Parameters, ref box);

            void UpdateRenderData(RenderDrawContext context, LightShadowMapTexture? shadowMapTexture)
            {
                _groupRenderer = new LightDirectionalGroupRenderer();

                ILightShadowMapShaderGroupData? shadowGroup = null;
                if (shadowMapTexture != null)
                {
                    _shadowType = shadowMapTexture.ShadowType;
                    _shadowMapRenderer = shadowMapTexture.Renderer;
                    shadowGroup = _shadowMapRenderer.CreateShaderGroupData(_shadowType.Value);
                }
                else
                {
                    _shadowType = 0;
                    _shadowMapRenderer = null;
                }
                _shaderGroup = _groupRenderer.CreateLightShaderGroup(context, shadowGroup);
            }
        }

        var viewInverse = Matrix.Invert(renderView.View);
        _renderVolumetricLightDirectional.Parameters.Set(TransformationKeys.ViewInverse, ref viewInverse);
        _renderVolumetricLightDirectional.Parameters.Set(TransformationKeys.Eye, new Vector4(viewInverse.TranslationVector, 1));

        Matrix projectionInverse;
        Matrix.Invert(ref renderView.Projection, out projectionInverse);
        _renderVolumetricLightDirectional.Parameters.Set(TransformationKeys.ProjectionInverse, projectionInverse);

        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.TransmittanceLUT, transmittanceLut);
        //_renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.SkyLuminanceLUT, skyLuminanceLut);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.DepthTexture, _depthShaderResourceView);
        _renderVolumetricLightDirectional.Parameters.Set(TerrainDataKeys.TerrainShadowMap, terrain?.GpuTextureManager?.ShadowMap);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.Atmosphere, atmosphere);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.Fog, fog);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.SunDirection, sunDirection);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.SunColor, sunColor);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.CameraPosition, cameraPosition);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.InvViewProjection, invViewProjection);
        _renderVolumetricLightDirectional.Parameters.Set(VolumetricLightDiretionalKeys.InvResolution, invViewSize);

        if (terrain != null && terrain.GpuTextureManager != null)
        {
            float invUnitsPerTexel = 1.0f / terrain.UnitsPerTexel;
            float invShadowMapsSize = invUnitsPerTexel * (1.0f / terrain.TerrainData.Header.Size);

            _renderVolumetricLightDirectional.Parameters.Set(TerrainDataKeys.InvShadowMapSize, invShadowMapsSize);
            _renderVolumetricLightDirectional.Parameters.Set(TerrainDataKeys.InvMaxHeight, 1.0f / terrain.TerrainData.Header.MaxHeight);
        }
        else
        {
            _renderVolumetricLightDirectional.Parameters.Set(TerrainDataKeys.InvShadowMapSize, 0.0f);
        }

        _renderVolumetricLightDirectional.Draw(context, "Atmosphere.RenderVolumetricLightDirectional");
    }
    #endregion

    #region Render Atmosphere LUT
    void RenderCameraVolume(RenderDrawContext context, AtmosphereParameters atmosphere, Matrix invViewProjection, Vector3 cameraPosition, Vector3 sunDirection, Color3 sunColor,
        Texture transmittanceLut, Texture mulitScatteredLuminanceLut)
    {
        if (_cameraVolumeLut == null)
            return;

        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.TransmittanceLUT, transmittanceLut);
        _cameraVolumeLut.Effect.Parameters.Set(AtmosphereCameraVolumeLutKeys.MultiScatteringLUT, mulitScatteredLuminanceLut);
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

public static class VolumetricLightDiretionalEffectKeys
{
    public static readonly PermutationParameterKey<ShaderSource> LightGroup = ParameterKeys.NewPermutation<ShaderSource>();
}
