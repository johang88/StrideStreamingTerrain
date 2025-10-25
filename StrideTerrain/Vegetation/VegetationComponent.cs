using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Rendering;

namespace StrideTerrain.Vegetation;

/// <summary>
/// Renders high density vegetation like trees with impostors
/// </summary>
[DefaultEntityComponentProcessor(typeof(VegetationProcessor))]
public class VegetationComponent : ScriptComponent
{
    public required Material ImpostorMaterial { get; set; }
    public required Model Model { get; set; }
    public Vector2 ImpostorSize { get; set; }
    public float ImpostorLodDistance { get; set; }

    [Display(Browsable = false)] public required string InstancesJson { get; set; }
}
