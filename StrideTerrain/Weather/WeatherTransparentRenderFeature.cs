using Stride.Core.Storage;
using Stride.Rendering;
using StrideTerrain.Weather.Effects;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;

namespace StrideTerrain.Weather;

public class WeatherTransparentRenderFeature :SubRenderFeature
{
    private ConstantBufferOffsetReference _offset;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _offset = ((RootEffectRenderFeature)RootRenderFeature).CreateFrameCBufferOffsetSlot(WeatherForwardRendererBaseKeys.Atmosphere.Name);
    }

    public override void PrepareEffectPermutations(RenderDrawContext context)
    {
        base.PrepareEffectPermutations(context);

        var hasWeather = context.RenderContext.Tags.TryGetValue(WeatherRenderObject.Current, out var weather);

        var renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;

        var renderEffects = RootRenderFeature.RenderData.GetData(renderEffectKey);
        int effectSlotCount = ((RootEffectRenderFeature)RootRenderFeature).EffectPermutationSlotCount;

        foreach (var renderObject in RootRenderFeature.RenderObjects)
        {
            var staticObjectNode = renderObject.StaticObjectNode;

            if (renderObject is not RenderMesh renderMesh)
                continue;

            var material = renderMesh.MaterialPass;
            var shouldRenderAtmosphereForRenderObject = material.HasTransparency && hasWeather;

            for (int i = 0; i < effectSlotCount; ++i)
            {
                var staticEffectObjectNode = staticObjectNode * effectSlotCount + i;
                var renderEffect = renderEffects[staticEffectObjectNode];

                // Skip effects not used during this frame
                if (renderEffect == null || !renderEffect.IsUsedDuringThisFrame(RenderSystem))
                    continue;

                renderEffect.EffectValidator.ValidateParameter(WeatherForwardShadingEffectParameters.EnableAerialPerspective, hasWeather);
                renderEffect.EffectValidator.ValidateParameter(WeatherForwardShadingEffectParameters.EnableVolumetricSunLight, shouldRenderAtmosphereForRenderObject);
                renderEffect.EffectValidator.ValidateParameter(WeatherForwardShadingEffectParameters.EnableHeightFog, hasWeather && weather?.Fog.Density > 0);
            }
        }
    }

    public unsafe override void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        if (!context.RenderContext.Tags.TryGetValue(WeatherRenderObject.Current, out var weather))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.TransmittanceLut, out var transmittanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.MultiScatteredLuminanceLut, out var multiScatteredLuminanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.SkyLuminanceLut, out var skyLuminanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherRenderFeature.CameraVolumeLut, out var cameraVolumeLut))
            return;

        if (weather == null)
            return;

        var logicalGroupKey = ((RootEffectRenderFeature)RootRenderFeature).CreateFrameLogicalGroup("Weather");
        foreach (var frameLayout in ((RootEffectRenderFeature)RootRenderFeature).FrameLayouts)
        {
            var chunkSizeOffset = frameLayout.GetConstantBufferOffset(_offset);
            if (chunkSizeOffset == -1)
                continue;

            var logicalGroup = frameLayout.GetLogicalGroup(logicalGroupKey);
            if (logicalGroup.Hash == ObjectId.Empty)
                continue;

            var resourceGroup = frameLayout.Entry.Resources;
            var mappedCB = resourceGroup.ConstantBuffer.Data;

            var perFrame = (PerFrameAtmosphere*)((byte*)mappedCB + chunkSizeOffset);
            perFrame->Atmosphere = weather.Atmosphere;
            perFrame->Fog = weather.Fog;
            perFrame->SunColor = weather.SunColor;
            perFrame->SunDirection = weather.SunDirection;

            resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 0, transmittanceLut);
            resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 1, skyLuminanceLut);
            resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 2, cameraVolumeLut);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerFrameAtmosphere
    {
        public AtmosphereParameters Atmosphere;
        public FogParameters Fog;
        public Vector3 SunDirection;
        public float Padding0;
        public Color3 SunColor;
        public float Padding1;
    }
}
