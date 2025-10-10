using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace StrideTerrain.Vegetation;

[DataContract]
[Display("Tree Displacement")]
public class MaterialTreeDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialTreeDisplacementFeature"));

        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}
