using Stride.Core;
using Stride.Rendering;
using StrideTerrain.Editor.Effects;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor.BlendModes;

[Display("Alpha Blend")]
public class BlendAlphaBlend : IBlendMode
{
    public string ShaderName => "BlendAlphaBlend";

    public bool IsDirty { get; private set; }

    public float Alpha;

    public void DrawUi()
    {
        IsDirty |= DragFloat("Alpha", ref Alpha, 0.1f, 0, 1);
    }

    public void SetShaderParameters(ParameterCollection parameterCollection)
    {
        IsDirty = false;
        parameterCollection.Set(BlendAlphaBlendKeys.Alpha, Alpha);
    }
}
