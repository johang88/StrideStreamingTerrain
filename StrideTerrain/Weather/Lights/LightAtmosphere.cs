using Stride.Core;
using Stride.Rendering.Lights;
using Stride.Rendering.Skyboxes;

namespace StrideTerrain.Weather.Lights;

/// <summary>
/// A light coming from the atmosphere (dynamic skybox).
/// </summary>
[DataContract("LightAtmosphere")]
[Display("Atmosphere")]
public class LightAtmosphere : IEnvironmentLight
{
    [DataMember(0)]  public Skybox? DynamicSkyBox { get; set; }

    public bool Update(RenderLight light)
    {
        return DynamicSkyBox != null;
    }
}
