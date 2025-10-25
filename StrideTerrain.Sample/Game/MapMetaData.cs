using Stride.Engine;
using Stride.Engine.Design;
using Stride.Graphics;

namespace StrideTerrain.Sample.Game;

[DefaultEntityComponentProcessor(typeof(MapMetaDataComponentProcessor))]
public class MapMetaData : ScriptComponent
{
    public Texture? MiniMap { get; set; }
}

public class MapMetaDataComponentProcessor : EntityProcessor<MapMetaData>
{
    public MapMetaData? Current { get; private set; }

    protected override void OnEntityComponentAdding(Entity entity, MapMetaData component, MapMetaData data)
    {
        base.OnEntityComponentAdding(entity, component, data);

        Current = data;
    }

    protected override void OnEntityComponentRemoved(Entity entity, MapMetaData component, MapMetaData data)
    {
        base.OnEntityComponentRemoved(entity, component, data);

        if (Current == data)
        {
            Current = null;
        }
    }
}