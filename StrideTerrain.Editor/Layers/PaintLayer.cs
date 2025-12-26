using Stride.Rendering;
using System;
using System.Text.Json.Serialization;

namespace StrideTerrain.Editor.Layers;

public class LayerPaint : ITerrainLayerType
{
    public string ShaderName => "LayerPaint";

    public bool IsDirty => throw new NotImplementedException();

    [JsonIgnore]
    public float BrushRadius;

    [JsonIgnore]
    public float BrushStrength;

    [JsonIgnore]
    public float BrushFalloff;

    public void DrawUi()
    {
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
    }
}
