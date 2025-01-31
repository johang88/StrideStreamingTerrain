using Stride.Core;
using Stride.Graphics;
using Stride.Rendering.Materials;
using Stride.Shaders;
using StrideTerrain.TerrainSystem.Effects.Material;

namespace StrideTerrain.TerrainSystem.Rendering.Materials;

[DataContract]
[Display("Terrain Diffuse")]
public class MaterialTerrainDiffuseFeature : MaterialFeature, IMaterialDiffuseFeature
{
    public Texture? DiffuseTextureArray { get; set; }
    public Texture? NormalTextureArray { get; set; }
    public Texture? RoughnessTextureArray { get; set; }
    public int Version { get; set; } // Just here so that a variable can easily be changed in the material to force a recompilation 

    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialTerrainDiffuse"));
        context.AddShaderSource(MaterialShaderStage.Pixel, mixin);

        context.AddStreamInitializer(MaterialShaderStage.Pixel, "TerrainMaterialStreamInitializer");

        context.Parameters.Set(TerrainMaterialStreamInitializerKeys.DiffuseArray, DiffuseTextureArray);
        context.Parameters.Set(TerrainMaterialStreamInitializerKeys.NormalArray, NormalTextureArray);
        context.Parameters.Set(TerrainMaterialStreamInitializerKeys.RoughnessArray, RoughnessTextureArray);
    }
}
