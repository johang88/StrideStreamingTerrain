using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Rendering;
using StrideTerrain.Editor.Effects;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor.Layers;

[Display("Circle")]
public class CircleLayer : ITerrainLayerType
{
    public string ShaderName => "LayerCircle";

    public bool IsDirty { get; private set; } = true;

    public float Height = 1;
    public float Radius = 64;
    public float Smoothness;
    public Vector2 Position;

    public void DrawUi()
    {
        IsDirty |= DragFloat("Radius", ref Radius, 1, 0, 10000);
        IsDirty |= DragFloat("Smoothness", ref Smoothness, 0.05f, 0, 1);
        IsDirty |= DragFloat("Height", ref Height, 0.05f, 0, 1);

        System.Numerics.Vector2 tmp = Position;
        IsDirty |= DragFloat2("Position", ref tmp);
        Position = tmp;
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
        IsDirty = false;

        parameterCollection.Set(LayerCircleKeys.Radius, Radius);
        parameterCollection.Set(LayerCircleKeys.Smoothness, Smoothness);
        parameterCollection.Set(LayerCircleKeys.Height, Height);
        parameterCollection.Set(LayerCircleKeys.Position, Position);
    }
}
