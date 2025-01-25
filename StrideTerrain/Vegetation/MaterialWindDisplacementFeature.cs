using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace StrideTerrain.Vegetation;

[DataContract]
[Display("Wind Displacement")]
public class MaterialWindDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialWindDisplacementFeature"));

        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}
