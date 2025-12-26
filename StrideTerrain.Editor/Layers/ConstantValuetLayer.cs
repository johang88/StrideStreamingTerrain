using Stride.Core;
using Stride.Rendering;
using StrideTerrain.Editor.Effects;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor.Layers;

[Display("Constant Value")]
public class ConstantValuetLayer : ITerrainLayerType
{
    public string ShaderName => "LayerConstantValue";

    public bool IsDirty { get; private set; } = true;

    public float Height;

    public void DrawUi()
    {
        IsDirty |= DragFloat("Height", ref Height);
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
        IsDirty = false;

        parameterCollection.Set(LayerConstantValueKeys.Value, Height);
    }
}
