using Stride.Engine;
using Stride.Engine.Design;

namespace StrideTerrain.Vegetation;

[DefaultEntityComponentProcessor(typeof(GrassProcessor))]
public class GrassComponent : ScriptComponent
{
    public ModelComponent? Model { get; set; }
    public int InstanceCount { get; set; } = 32 * 32;
}
