using Stride.Core;
using Stride.Rendering;

namespace StrideTerrain.Editor.BlendModes;

[Display("None")]
public class BlendNone : IBlendMode
{
    public string ShaderName => "TerrainBlendMode";

    public bool IsDirty => false;

    public void DrawUi()
    {
    }

    public void SetShaderParameters(ParameterCollection parameterCollection)
    {
    }
}
