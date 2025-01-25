using System;

namespace StrideTerrain.TerrainSystem.Streaming;

public interface IStreamingRequest
{
    int ChunkIndex { get; }
    bool TryGetHeightmap(out ReadOnlySpan<byte> data);
    bool TryGetNormalMap(out ReadOnlySpan<byte> data);
}
