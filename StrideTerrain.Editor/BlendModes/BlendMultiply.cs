using Stride.Core;
using Stride.Rendering;

namespace StrideTerrain.Editor.BlendModes;

[Display("Multiply")]
public class BlendMultiply : IBlendMode
{
    public string ShaderName => "BlendMultiply";

    public bool IsDirty => false;

    public void DrawUi()
    {
    }

    public void SetShaderParameters(ParameterCollection parameterCollection)
    {
    }
}
