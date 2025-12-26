using Stride.Graphics;
using Stride.Rendering.Materials;
using Stride.Shaders;
using StrideTerrain.Editor.Effects;

namespace StrideTerrain.Editor.Rendering;

public class EditorTerrainDiffuseFeature : MaterialFeature, IMaterialDiffuseFeature
{
    public Texture? DiffuseTextureArray { get; set; }
    public Texture? NormalTextureArray { get; set; }
    public Texture? RoughnessTextureArray { get; set; }

    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("EditorTerrainDiffuse"));
        context.AddShaderSource(MaterialShaderStage.Pixel, mixin);

        context.AddStreamInitializer(MaterialShaderStage.Pixel, "EditorTerrainMaterialStreamInitializer");

        context.Parameters.Set(EditorTerrainMaterialStreamInitializerKeys.DiffuseArray, DiffuseTextureArray);
        context.Parameters.Set(EditorTerrainMaterialStreamInitializerKeys.NormalArray, NormalTextureArray);
        context.Parameters.Set(EditorTerrainMaterialStreamInitializerKeys.RoughnessArray, RoughnessTextureArray);
    }
}
