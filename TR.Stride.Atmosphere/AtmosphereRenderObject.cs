using Stride.Core.Mathematics;
using Stride.Rendering;

namespace TR.Stride.Atmosphere;

public class AtmosphereRenderObject : RenderObject
{
    public AtmosphereComponent Component = null;

    public Vector3? PreviousLightDiretion = null;
}
