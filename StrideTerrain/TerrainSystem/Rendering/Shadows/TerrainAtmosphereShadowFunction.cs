using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Rendering;
using StrideTerrain.TerrainSystem.Effects.Shadows;
using StrideTerrain.TerrainSystem.Rendering;
using TR.Stride.Atmosphere;

namespace StrideTerrain.TerrainSystem.Rendering.Shadows;

[DataContract]
public class TerrainAtmosphereShadowFunction : IAtmosphereShadowFunction
{
    public string Shader => "TerrainAtmosphereShadow";

    public void UpdateParameters(RenderDrawContext context, AtmosphereComponent component, ParameterCollection parameters)
    {
        if (context.Tags.TryGetValue(TerrainRenderFeature.TerrainList, out var terrains) && terrains.Count == 1)
        {
            var terrain = terrains[0];
            if (terrain.ShadowMap == null)
                return;

            float invUnitsPerTexel = 1.0f / terrain.UnitsPerTexel;
            float invShadowMapsSize = invUnitsPerTexel * (1.0f / terrain.TerrainData.Header.Size);

            parameters.Set(TerrainAtmosphereShadowKeys.TerrainWorldSize, new Vector4(invShadowMapsSize, invShadowMapsSize, 1.0f / terrain.TerrainData.Header.MaxHeight, 0.0f));
            parameters.Set(TerrainAtmosphereShadowKeys.UseTerrainShadowMap, 1);
            parameters.Set(TerrainAtmosphereShadowKeys.TerrainShadowMap, terrain.ShadowMap);
        }
        else
        {
            parameters.Set(TerrainAtmosphereShadowKeys.UseTerrainShadowMap, 0);
        }
    }
}
