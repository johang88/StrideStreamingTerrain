using Stride.Core;
using Stride.Core.Mathematics;
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
    [DataMember(0), Obsolete] public Texture? Heightmap { get; set; }

    [DataMember(1)] public Material? Material { get; set; }

    [DataMember(2), DefaultValue(100.0f)] public float MaxHeight { get; set; } = 100.0f;

    [DataMember(3)]
    public UrlReference? TerrainData { get; set; }

    [DataMember(4)]
    public UrlReference? TerrainStreamingData { get; set; }

    [DataMember(10), DefaultValue(64), Obsolete] public int ChunkSize { get; set; } = 64;

    /// <summary>
    /// UnitsPerTexel * Heightmap.Width = TerrainWorldSize
    /// </summary>
    [DataMember(11), DefaultValue(1.0f)] public float UnitsPerTexel { get; set; } = 1.0f;
    
    /// <summary>
    /// Distance at which the highest resolution lod will be active
    /// </summary>
    [DataMember(12), DefaultValue(64.0f)] public float Lod0Distance { get; set; } = 64.0f;

    /// <summary>
    /// Maximum lod level
    /// -1 = autmatically calculated for single visible chunk
    /// </summary>
    [DataMember(13), DefaultValue(-1)] public int MaximumLod { get; set; } = -1;

    [DataMember(14), DefaultValue(0)] public int MinimumLod { get; set; } = 0;

    [DataMember(100), DefaultValue(false)] public bool FreezeCamera { get; set; } = false;

    [DataMember(101), DefaultValue(true)] public bool FrustumCull { get; set; } = true;

    [DataMember(102), DefaultValue(false)] public bool FreezeFrustum { get; set; } = false;
}
