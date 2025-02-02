using Stride.Core;
using Stride.Core.Serialization;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.ComponentModel;

namespace StrideTerrain.TerrainSystem;

[DataContract]
[Display("Terrain", "Terrain", Expand = ExpandRule.Once)]
[DefaultEntityComponentRenderer(typeof(TerrainProcessor))]
public class TerrainComponent : EntityComponent
{
    [DataMember(1)] public Material? Material { get; set; }

    [DataMember(2)]
    public UrlReference? TerrainData { get; set; }

    [DataMember(3)]
    public UrlReference? TerrainStreamingData { get; set; }

    /// <summary>
    /// Distance at which the highest resolution lod will be active
    /// </summary>
    [DataMember(10), DefaultValue(64.0f)] public float Lod0Distance { get; set; } = 64.0f;

    /// <summary>
    /// Maximum lod level
    /// -1 = autmatically calculated for single visible chunk
    /// </summary>
    [DataMember(11), DefaultValue(-1)] public int MaximumLod { get; set; } = -1;

    [DataMember(12), DefaultValue(0)] public int MinimumLod { get; set; } = 0;

    [DataMember(15)] public RenderGroup RenderGroup { get; set; } = RenderGroup.Group29;
}
