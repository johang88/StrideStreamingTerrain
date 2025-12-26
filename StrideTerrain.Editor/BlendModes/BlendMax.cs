using Stride.Core;
using Stride.Rendering;

namespace StrideTerrain.Editor.BlendModes;

[Display("Max")]
public class BlendMax : IBlendMode
{
    public string ShaderName => "BlendMax";

    public bool IsDirty => false;

    public void DrawUi()
    {
    }

    public void SetShaderParameters(ParameterCollection parameterCollection)
    {
    }
}
