using System;

namespace StrideTerrain.TerrainSystem.Streaming;

[Flags]
public enum PartsToLoad : int
{
    Heightmap = 0x01,
    NormalMap = 0x02,
    All = Heightmap | NormalMap
}