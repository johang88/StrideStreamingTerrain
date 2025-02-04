using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;

namespace StrideTerrain.Weather;

[DataContract, DefaultEntityComponentRenderer(typeof(WeatherEntityProcessor))]
public class WeatherComponent : EntityComponent
{
    public LightComponent? Sun { get; set; }

    public AtmosphereParameters Atmosphere { get; set; } = new();

    public FogParameters Fog { get; set; } = new();
}
