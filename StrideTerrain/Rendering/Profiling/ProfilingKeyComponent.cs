using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.Engine.Design;

namespace StrideTerrain.Rendering.Profiling;

[DefaultEntityComponentRenderer(typeof(ProfilingKeyRenderProcessor))]
public class ProfilingKeyComponent : EntityComponent
{
    [DataMemberIgnore] public ProfilingKey? ProfilingKey { get; set; }
}
