using Stride.Rendering.Materials;
using Stride.Shaders;

namespace StrideTerrain.Editor.Rendering;

public class EditorTerrainDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("EditorTerrainDisplacement"));

        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}

