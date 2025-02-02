using Stride.Core.Mathematics;
using Stride.Rendering;

namespace StrideTerrain.Weather;

public class WeatherRenderObject : RenderObject
{
    public Vector3 SunDirection;
    public Color3 SunColor;
    public AtmosphereParameters Atmosphere;
    public FogParameters Fog;
    public ParameterCollection? SkyBoxSpecularLightingParameters;
}
