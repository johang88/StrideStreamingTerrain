using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace StrideTerrain.Vegetation;

[DataContract]
[Display("Impostor Displacement")]
public class MaterialImpostorDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialImpostorDisplacementFeature"));

        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}
