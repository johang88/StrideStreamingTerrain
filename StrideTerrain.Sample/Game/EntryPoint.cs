using Stride.Engine.Design;
using Stride.Engine;

namespace StrideTerrain.Sample.Game;

[DefaultEntityComponentProcessor(typeof(EntryPointProcessor))]
public class EntryPoint : ScriptComponent
{
    public bool Default { get; set; }
}