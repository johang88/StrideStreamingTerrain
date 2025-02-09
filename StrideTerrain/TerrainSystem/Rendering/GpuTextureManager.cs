using Stride.Graphics;
using StrideTerrain.Common;
using StrideTerrain.TerrainSystem.Streaming;
using System;
using System.Collections.Generic;

namespace StrideTerrain.TerrainSystem.Rendering;

/// <summary>
/// Manages all textures needed for the terrain including height, normal atlases and shadow map.
/// </summary>
public class GpuTextureManager : IDisposable
{
    public StreamingTextureAtlas Heightmap { get; private init; }
    public StreamingTextureAtlas NormalMap { get; private init; }
    public StreamingTextureAtlas ControlMap { get; private init; }
    public Texture ShadowMap { get; private init; }

    private readonly ChunkData[] _chunks;

    private readonly IStreamingManager _streamingManager;

    private readonly Queue<int> _freeList;
    private readonly Stack<int> _pendingChunks = [];
    private Dictionary<int, int> _chunkToTextureIndex = [];
    private int _frameCounter = 0;

    public bool InvalidateShadowMap { get; set; }

    public GpuTextureManager(TerrainData terrain, GraphicsDevice graphicsDevice, int atlasSize, IStreamingManager streamingManager)
    {
        _streamingManager = streamingManager;

        var chunkTextureSize = Math.Max(terrain.Header.ChunkTextureSize, terrain.Header.NormalMapTextureSize);

        Heightmap = new StreamingTextureAtlas(graphicsDevice, PixelFormat.R16_UNorm, atlasSize, chunkTextureSize, terrain.Header.ChunkTextureSize);
        NormalMap = new StreamingTextureAtlas(graphicsDevice, terrain.Header.CompressedNormalMap ? PixelFormat.BC5_UNorm : PixelFormat.R8G8_UNorm, atlasSize, chunkTextureSize, terrain.Header.NormalMapTextureSize);
        ControlMap = new StreamingTextureAtlas(graphicsDevice, PixelFormat.R16_UInt, atlasSize, chunkTextureSize, terrain.Header.ChunkTextureSize);
        ShadowMap = Texture.New2D(graphicsDevice, TerrainRuntimeData.ShadowMapSize, TerrainRuntimeData.ShadowMapSize, PixelFormat.R10G10B10A2_UNorm, TextureFlags.UnorderedAccess | TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        var chunksPerRow = Math.Min(Heightmap.ChunksPerRow, NormalMap.ChunksPerRow);

        _freeList = new Queue<int>(Heightmap.ChunksPerRow * Heightmap.ChunksPerRow);
        _chunks = new ChunkData[Heightmap.ChunksPerRow * Heightmap.ChunksPerRow];
        for (var i = 0; i < _chunks.Length; i++)
        {
            _chunks[i] = new ChunkData
            {
                ChunkIndex = -1,
                LastActiveFrame = -1
            };

            _freeList.Enqueue(i);
        }
    }

    public void Dispose()
    {
        Heightmap?.Dispose();
        NormalMap?.Dispose();
        ControlMap?.Dispose();
        ShadowMap?.Dispose();
    }

    public void Update(GraphicsContext graphicsContext)
    {
        // Free chunks if possible and needed.
        if (_freeList.Count < 16)
        {
            // Try to free up some chunks ... should be more of an LRU but whatever.
            for (var i = 0; i < _chunks.Length; i++)
            {
                var chunk = _chunks[i];
                if (chunk.LastActiveFrame < (_frameCounter - 30) && chunk.State == ChunkState.Resident)
                {
                    _chunkToTextureIndex.Remove(chunk.ChunkIndex);
                    chunk.State = ChunkState.Free;
                    _freeList.Enqueue(i);

                    if (_freeList.Count >= 16)
                        break;
                }
            }
        }

        // Process pending requests
        while (_pendingChunks.Count > 0)
        {
            var chunkDataIndex = _pendingChunks.Pop();

            // Check if already loaded somehow ... this case should not happen.
            if (_chunkToTextureIndex.TryGetValue(chunkDataIndex, out var chunkIndex))
            {
                // Useful to know as I would consider this a bug.
                if (_chunks[chunkIndex].State != ChunkState.Resident)
                {
                    throw new InvalidOperationException("invalid chunk state!");
                }

                continue;
            }

            if (_freeList.Count == 0)
                continue;

            chunkIndex = _freeList.Dequeue();
            _chunkToTextureIndex[chunkDataIndex] = chunkIndex;

            var chunk = _chunks[chunkIndex];
            if (chunk.ChunkIndex == chunkDataIndex)
            {
                // Already loaded, lucky! We assume that the atlas data has not been overwritten.
                chunk.State = ChunkState.Resident;
                continue;
            }

            chunk.ChunkIndex = chunkDataIndex;
            chunk.State = ChunkState.Loading;

            _streamingManager.Request(ChunksToLoad.Heightmap | ChunksToLoad.NormalMap | ChunksToLoad.ControlMap, chunkDataIndex, StreamingCompletedCallback, graphicsContext);
        }

        _frameCounter++;
    }

    private void StreamingCompletedCallback(IStreamingRequest streamingRequest, object? callbackData)
    {
        if (!_chunkToTextureIndex.TryGetValue(streamingRequest.ChunkIndex, out var chunkDataIndex))
            return; // Should never happen.

        // Not super happy with this, should probably have a queue for chunks to make resident.
        // But would need to copy over the data to a temp buffer in that case, so good enough for now!
        // Also streaming callbacks are done on main thread so it should be fine!
        var graphicsContext = (GraphicsContext?)callbackData;
        if (graphicsContext == null)
            return; // Can't happen

        if (streamingRequest.TryGetHeightmap(out var heightmap))
            Heightmap.UpdateChunk(graphicsContext, heightmap, chunkDataIndex);

        if (streamingRequest.TryGetNormalMap(out var normalMap))
            NormalMap.UpdateChunk(graphicsContext, normalMap, chunkDataIndex);

        if (streamingRequest.TryGetControlMap(out var controlMap))
            ControlMap.UpdateChunk(graphicsContext, controlMap, chunkDataIndex);

        var chunk = _chunks[chunkDataIndex];
        chunk.State = ChunkState.Resident;

        InvalidateShadowMap = true;
    }

    public bool RequestChunk(int chunkIndex)
    {
        if (!_chunkToTextureIndex.TryGetValue(chunkIndex, out var textureIndex))
        {
            // Mark for loading
            _pendingChunks.Push(chunkIndex);
            return false;
        }

        _chunks[textureIndex].LastActiveFrame = _frameCounter;
        return _chunks[textureIndex].State == ChunkState.Resident;
    }

    /// <summary>
    /// Make sure chunk is resident before calling, value is only valid until next Update().
    /// </summary>
    public int GetTextureIndex(int chunkIndex)
        => _chunkToTextureIndex[chunkIndex];

    private sealed class ChunkData
    {
        public int ChunkIndex;
        public int LastActiveFrame;
        public ChunkState State;
    }

    private enum ChunkState
    {
        Free,
        Loading,
        Resident
    }
}
