using System;

namespace StrideTerrain.TerrainSystem.Streaming;

[Flags]
public enum ChunksToLoad : int
{
    Heightmap = 0x01,
    NormalMap = 0x02,
    ControlMap = 0x04,
    All = Heightmap | NormalMap | ControlMap
}