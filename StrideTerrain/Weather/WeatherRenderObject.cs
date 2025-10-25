using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Rendering;
using Stride.Rendering.Lights;

namespace StrideTerrain.Weather;

public class WeatherRenderObject : RenderObject
{
    public static readonly PropertyKey<WeatherRenderObject> Current = new("WeatherRenderObject.Current", typeof(WeatherRenderObject));

    public Vector3 SunDirection;
    public Color3 SunColor;
    public AtmosphereParameters Atmosphere;
    public FogParameters Fog;
    public CloudParameters Clouds;
    public RenderLight? Sun;
}
