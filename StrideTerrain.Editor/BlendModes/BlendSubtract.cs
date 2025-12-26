using Stride.Core;
using Stride.Rendering;
using StrideTerrain.Editor.Effects;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor.BlendModes;

[Display("Subtract")]
public class BlendSubtract : IBlendMode
{
    public string ShaderName => "BlendSubtract";

    public bool IsDirty { get; private set; }

    public float Strength = 1;

    public void DrawUi()
    {
        IsDirty |= DragFloat("Strength", ref Strength, 0.1f, 0, 1);
    }

    public void SetShaderParameters(ParameterCollection parameterCollection)
    {
        IsDirty = false;
        parameterCollection.Set(BlendSubtractKeys.Strength, Strength);
    }
}
