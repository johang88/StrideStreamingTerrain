using Stride.Engine;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace StrideTerrain.Sample.Game;

public class EntryPointProcessor : EntityProcessor<EntryPoint>
{
    public Entity? Default { get; private set; }
    public List<Entity> EntryPoints { get; } = [];

    public (Vector3 Position, Quaternion Rotation) GetEntryPoint(string? name)
    {
        var entryPoint = Default;
        if (!string.IsNullOrEmpty(name))
        {
            entryPoint = EntryPoints.FirstOrDefault(x => x.Name == name) ?? Default;
        }

        return (entryPoint?.Transform.Position ?? new(), entryPoint?.Transform.Rotation ?? new());
    }

    protected override void OnEntityComponentAdding(Entity entity, EntryPoint component, EntryPoint data)
    {
        base.OnEntityComponentAdding(entity, component, data);

        EntryPoints.Add(entity);

        if (component.Default)
        {
            Default = entity;
        }
    }

    protected override void OnEntityComponentRemoved(Entity entity, EntryPoint component, EntryPoint data)
    {
        base.OnEntityComponentRemoved(entity, component, data);

        EntryPoints.Remove(entity);

        if (entity == Default)
        {
            Default = null;
        }
    }
}