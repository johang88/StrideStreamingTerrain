using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Shadows;
using Stride.Shaders;
using StrideTerrain.Rendering.ReverseZ;

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

        public override ShaderClassSource CreateShaderSource(int lightCurrentCount)
        {
            var isDepthRangeAuto = (ShadowType & LightShadowType.DepthRangeAuto) != 0;
            return new ShaderClassSource(EffectName, cascadeCount, lightCurrentCount, (ShadowType & LightShadowType.BlendCascade) != 0, isDepthRangeAuto, (ShadowType & LightShadowType.Debug) != 0, (ShadowType & LightShadowType.ComputeTransmittance) != 0);
        }
    }
}
