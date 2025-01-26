using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Stride.Shaders;
using StrideTerrain.Rendering;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Effects.Shadows;
using StrideTerrain.TerrainSystem.Rendering;

namespace StrideTerrain.TerrainSystem.Rendering.Shadows;

/// <summary>
/// Injects the terrain shadow map, rendering is done in `TerrainShadowMapRenderer`
/// </summary>
public class TerrainDirectionalShadowMapRenderer : LightDirectionalShadowMapRendererReverseZ
{
    public override void Collect(RenderContext context, RenderView sourceView, LightShadowMapTexture lightShadowMap)
    {
        base.Collect(context, sourceView, lightShadowMap);
    }

    public override ILightShadowMapShaderGroupData CreateShaderGroupData(LightShadowType shadowType)
            => new TerrainShaderGroupData(shadowType);

    private class TerrainShaderGroupData(LightShadowType lightShadowType) : ShaderGroupData(lightShadowType)
    {
        public const string EffectName = "ShadowMapReceiverTerrainDrectional";

        private ValueParameterKey<Vector4>? _terrainWorldSizeKey;
        private ValueParameterKey<int>? _useTerrainShadowMapKey;
        private ObjectParameterKey<Texture>? _terrainShadowMapKey;

        public override ShaderClassSource CreateShaderSource(int lightCurrentCount)
        {
            var isDepthRangeAuto = (ShadowType & LightShadowType.DepthRangeAuto) != 0;
            return new ShaderClassSource(EffectName, cascadeCount, lightCurrentCount, (ShadowType & LightShadowType.BlendCascade) != 0, isDepthRangeAuto, (ShadowType & LightShadowType.Debug) != 0, (ShadowType & LightShadowType.ComputeTransmittance) != 0);
        }

        public override void UpdateLayout(string compositionKey)
        {
            base.UpdateLayout(compositionKey);

            _terrainWorldSizeKey = ShadowMapReceiverTerrainDrectionalKeys.TerrainWorldSize.ComposeWith(compositionKey);
            _useTerrainShadowMapKey = ShadowMapReceiverTerrainDrectionalKeys.UseTerrainShadowMap.ComposeWith(compositionKey);
            _terrainShadowMapKey = ShadowMapReceiverTerrainDrectionalKeys.TerrainShadowMap.ComposeWith(compositionKey);
        }

        public override void ApplyViewParameters(RenderDrawContext context, ParameterCollection parameters, Stride.Core.Collections.FastListStruct<LightDynamicEntry> currentLights)
        {
            base.ApplyViewParameters(context, parameters, currentLights);

            if (context.Tags.TryGetValue(TerrainRenderFeature.TerrainList, out var terrains) && terrains.Count == 1)
            {
                var terrain = terrains[0];
                if (terrain.GpuTextureManager?.ShadowMap == null)
                    return;

                float invUnitsPerTexel = 1.0f / terrain.UnitsPerTexel;
                float invShadowMapsSize = invUnitsPerTexel * (1.0f / terrain.TerrainData.Header.Size);

                // Apply shader view parameters
                parameters.Set(_terrainWorldSizeKey, new Vector4(invShadowMapsSize, invShadowMapsSize, 1.0f / terrain.TerrainData.Header.MaxHeight, 0.0f));
                parameters.Set(_useTerrainShadowMapKey, 1);
                parameters.Set(_terrainShadowMapKey, terrain.GpuTextureManager.ShadowMap);
            }
            else
            {
                parameters.Set(_useTerrainShadowMapKey, 0);
            }
        }
    }
}
