using Stride.Core.Storage;
using Stride.Rendering;
using StrideTerrain.Weather.Effects;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;

namespace StrideTerrain.Weather;

public class WeatherTransparentRenderFeature :SubRenderFeature
{
    private WeatherRenderFeature? _weatherRenderFeature;

    private ConstantBufferOffsetReference _offset;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _offset = ((RootEffectRenderFeature)RootRenderFeature).CreateFrameCBufferOffsetSlot(WeatherForwardRendererBaseKeys.Atmosphere.Name);
    }

    public override void PrepareEffectPermutations(RenderDrawContext context)
    {
        base.PrepareEffectPermutations(context);

        // Try finding root atmosphere render feature
        _weatherRenderFeature = null;
        foreach (var renderFeature in ((RootEffectRenderFeature)RootRenderFeature).RenderSystem.RenderFeatures)
        {
            if (renderFeature is WeatherRenderFeature weatherRenderFeature)
            {
                _weatherRenderFeature = weatherRenderFeature;
            }
        }

        var renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;

        var renderEffects = RootRenderFeature.RenderData.GetData(renderEffectKey);
        int effectSlotCount = ((RootEffectRenderFeature)RootRenderFeature).EffectPermutationSlotCount;

        foreach (var renderObject in RootRenderFeature.RenderObjects)
        {
            var staticObjectNode = renderObject.StaticObjectNode;

            if (renderObject is not RenderMesh renderMesh)
                continue;

            var material = renderMesh.MaterialPass;
            var shouldRenderAtmosphereForRenderObject = material.HasTransparency && _weatherRenderFeature != null;

            for (int i = 0; i < effectSlotCount; ++i)
            {
                var staticEffectObjectNode = staticObjectNode * effectSlotCount + i;
                var renderEffect = renderEffects[staticEffectObjectNode];

                // Skip effects not used during this frame
                if (renderEffect == null || !renderEffect.IsUsedDuringThisFrame(RenderSystem))
                    continue;

                renderEffect.EffectValidator.ValidateParameter(WeatherForwardShadingEffectParameters.Enable, shouldRenderAtmosphereForRenderObject);
            }
        }
    }

    public unsafe override void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        if (_weatherRenderFeature == null)
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.TransmittanceLut, out var transmittanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.MultiScatteredLuminanceLut, out var multiScatteredLuminanceLut))
            return;

        if (!context.RenderContext.Tags.TryGetValue(WeatherLutRenderer.SkyLuminanceLut, out var skyLuminanceLut))
            return;

        var weather = _weatherRenderFeature.ActiveWeatherRenderObject;
        var cameraVolumeLut = _weatherRenderFeature.CameraVolumeLut;

        if (weather == null || cameraVolumeLut == null)
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
