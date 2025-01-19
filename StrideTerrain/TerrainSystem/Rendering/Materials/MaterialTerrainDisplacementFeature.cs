using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace StrideTerrain.TerrainSystem.Rendering.Materials;

[DataContract]
[Display("Terrain Displacement")]
public class MaterialTerrainDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialTerrainDisplacement"));

        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}
