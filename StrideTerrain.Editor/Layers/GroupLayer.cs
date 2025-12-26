using Stride.Core;
using Stride.Rendering;

namespace StrideTerrain.Editor.Layers;

[Display("Group")]
public class GroupLayer : ITerrainLayerType
{
    public string ShaderName => "LayerGroup";

    public bool IsDirty => false;

    public void DrawUi()
    {
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
    }
}
