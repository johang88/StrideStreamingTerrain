using Stride.Core;
using System.Collections.Generic;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Rendering;

namespace StrideTerrain.Vegetation;

/// <summary>
/// Renders high density vegetation like trees with impostors.
///
/// The impostor atlas is baked from <see cref="Model"/> at load time, so the billboard always
/// matches the mesh it hands over to. <see cref="ImpostorMaterial"/> supplies the shading - it
/// should use the Impostor Atlas Diffuse / Normal material nodes, which read the baked atlas
/// rather than a texture asset.
/// </summary>
[DefaultEntityComponentProcessor(typeof(VegetationProcessor))]
public class VegetationComponent : ScriptComponent
{
    public required Material ImpostorMaterial { get; set; }
    public required Model Model { get; set; }

    /// <summary>
    /// Frames per axis in the baked hemi-octahedral atlas. 8 gives 64 frames, which is enough that
    /// the three way blend is imperceptible; raising it costs bake time and VRAM quadratically.
    /// </summary>
    [DataMember] public int ImpostorGridSize { get; set; } = 8;

    /// <summary>Resolution of a single atlas frame. Total atlas is this times ImpostorGridSize.</summary>
    [DataMember] public int ImpostorFrameResolution { get; set; } = 256;

    /// <summary>
    /// Distance at which real meshes stop being drawn and the impostor takes over fully. Now that
    /// the mesh LOD chain carries the mid range cheaply, this sits far enough out that the impostor
    /// is only ever seen small - the far LODs cover the band where a billboard would read as flat.
    /// </summary>
    [DataMember] public float ImpostorLodDistance { get; set; } = 120.0f;

    /// <summary>
    /// Width of the cross fade band ending at <see cref="ImpostorLodDistance"/>. Mesh and impostor
    /// are both drawn here, dithered against each other, so the handover has no visible pop.
    /// </summary>
    [DataMember] public float ImpostorFadeRange { get; set; } = 12.0f;

    /// <summary>
    /// Handover distance for each LOD level, nearest first. Leave empty to derive them from
    /// <see cref="ImpostorLodDistance"/> - the fallback spaces levels quadratically, matching how
    /// screen coverage falls off, rather than evenly.
    /// </summary>
    [DataMember] public List<float> LodDistances { get; set; } = [];

    [Display(Browsable = false)] public required string InstancesJson { get; set; }
}
