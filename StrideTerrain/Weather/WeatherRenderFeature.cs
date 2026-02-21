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

    protected override void InitializeCore()
    {
        base.InitializeCore();

        SortKey = 0; // Should always draw first

        _cameraVolumeLut = new(this, Context, "AtmosphereCameraVolumeLut", 32, 32, 32, PixelFormat.R16G16B16A16_Float);

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

        _renderVolumetricLightDirectional = new ImageEffectShader("VolumetricLightDiretionalEffect");
        _renderVolumetricLightDirectional.Initialize(Context);
        _renderVolumetricLightDirectional.DisposeBy(this);
        _renderVolumetricLightDirectional.BlendState = BlendStates.AlphaBlend;

        _renderAerialPerspectiveEffect = new(Context) { ShaderSourceName = "AtmosphereRenderAerialPerspective" };
        _renderAerialPerspectiveEffect.DisposeBy(this);

        _spriteBatch = new(Context.GraphicsDevice);
        _spriteBatch.DisposeBy(this);
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
            // We only draw the weather effects in the transparent render stage as a post effect over the opaque stage,
            // this also gives us easy access to the depth buffer.

            // Might be useful to use this in the future if more effects that read the aerial perspective are added (like clouds maybe?), in that case the forward effect should be modified to not sample the aerial perspective.
            //var aerialPerspectiveRenderTarget = context.RenderContext.Allocator.GetTemporaryTexture2D((int)viewSize.X, (int)viewSize.Y, PixelFormat.R16G16B16A16_Float, TextureFlags.UnorderedAccess | TextureFlags.ShaderResource); ;
            //RenderAerialPerspectiveTexture(context, atmosphere, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize, viewSize, aerialPerspectiveRenderTarget);

            RenderSky(context, atmosphere, fog, clouds, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize, transmittanceLut, multiScatteredLuminanceLut, skyLuminanceLut, skyViewLut);
            //RenderAerialPerspective(context, aerialPerspectiveRenderTarget);
            //RenderFog(context, atmosphere, fog, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize);

            context.RenderContext.Tags.TryGetValue(CubeMapRenderer.IsRenderingCubemap, out var isRenderingCubeMap);
            //if (!isRenderingCubeMap)
            //    RenderVolumetricLightDirectional(context, atmosphere, fog, sunDirection, sunColor, cameraPosition, invViewProjection, invViewSize, transmittanceLut, skyLuminanceLut, renderView, renderObject.Sun);

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
        Matrix invViewProjection, Vector2 invViewSize, Texture transmittanceLut, Texture mulitScatteredLuminanceLut, Texture skyLuminanceLut, Texture skyViewLut)
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
