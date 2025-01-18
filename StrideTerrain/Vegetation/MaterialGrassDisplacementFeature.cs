using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace StrideTerrain.Vegetation;

[DataContract]
[Display("Grass Displacement")]
public class MaterialGrassDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialGrassDisplacementFeature"));

        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}
