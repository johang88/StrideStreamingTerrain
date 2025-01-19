using Stride.Core;
using Stride.Rendering;

namespace TR.Stride.Atmosphere;

public interface IAtmosphereShadowFunction
{
    string Shader { get; }
    void UpdateParameters(RenderDrawContext context, AtmosphereComponent component, ParameterCollection parameters);
}


[DataContract]
public class AtmosphereShadowFunctionNone : IAtmosphereShadowFunction
{
    public string Shader => "AtmosphereShadowNone";

    public void UpdateParameters(RenderDrawContext context, AtmosphereComponent component, ParameterCollection parameters)
    {
    }
}
