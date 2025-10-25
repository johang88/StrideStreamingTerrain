using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Reflection;
using Stride.Core.Serialization;
using Stride.Data;
using Stride.Engine;
using System;

namespace StrideTerrain.Sample;

[DataContract]
[Display("Game.Core")]
[ObjectFactory(typeof(CoreDataSettingsFactory))]
public class CoreDataSettings : Configuration
{
    public UrlReference<Prefab> PlayerCamera { get; set; } = null!;
    public UrlReference<Prefab> Player { get; set; } = null!;
    public UrlReference<Scene> InitialScene { get; set; } = null!;
}

public class CoreDataSettingsFactory : IObjectFactory
{
    public object New(Type type)
        => new CoreDataSettings();
}