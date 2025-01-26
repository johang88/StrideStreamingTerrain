using System;

namespace StrideTerrain.TerrainSystem.Streaming;

public interface IStreamingRequest
{
    int ChunkIndex { get; }
    bool TryGetHeightmap(out Span<byte> data);
    bool TryGetNormalMap(out Span<byte> data);
}
