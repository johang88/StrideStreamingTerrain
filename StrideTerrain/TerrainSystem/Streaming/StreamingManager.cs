using StrideTerrain.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace StrideTerrain.TerrainSystem.Streaming;

/// <summary>
/// Manages streaming of the terrain data in a background thread.
/// 
/// This class does not handle any caching so it's up to the callers to implement caching as needed.
/// Might be something to consider in the future or a wrapper class that provides a simple LUR cache
/// that can be used by both gpu and physics streaming systems.
/// 
/// It might make sense to switch to the inbuilt streaming system in Stride after/if a custom asset type
/// is implemented for the terrain. The inbuilt streaming system does not support files sizes above
/// int.MaxValue and neither does virtual file system stream.
/// </summary>
public sealed class StreamingManager : IDisposable, IStreamingManager
{
    private Thread? _ioThread;
    private readonly CancellationTokenSource _cts = new();

    private readonly TerrainData _terrain;
    private readonly Stream _stream;
    private readonly long _baseOffset;

    private readonly BlockingCollection<StreamingRequest> _pendingRequests = [];
    private readonly ConcurrentQueue<StreamingRequest> _completionQueue = [];
    private readonly Stack<StreamingRequest> _requestPool = new(256);

    public StreamingManager(TerrainData terrain, ITerrainDataProvider terrainDataProvider)
    {
        _terrain = terrain;

        var (stream, baseOffset) = terrainDataProvider.OpenStreamingData();
        _stream = stream;
        _baseOffset = baseOffset;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _cts.Cancel();

        _ioThread?.Join();
        _ioThread = null;
    }

    /// <summary>
    /// Should be called each frame on the main thread in order for the completion callbacks to be invoked.
    /// </summary>
    /// <param name="maxCompletionsToProcess">Maximum number of completions to process during a single frame. Set to -1 for unlimited.</param>
    public void ProcessPendingCompletions(int maxCompletionsToProcess = -1)
    {
        if (_ioThread == null)
        {
            _ioThread = new Thread(StreamingThread);
            _ioThread.Start();
        }

        while (maxCompletionsToProcess > 0 || maxCompletionsToProcess == -1)
        {
            maxCompletionsToProcess--;

            if (!_completionQueue.TryDequeue(out var streamingRequest))
                break;

            streamingRequest.Callback?.Invoke(streamingRequest, streamingRequest.CallbackData);
            _requestPool.Push(streamingRequest);
        }
    }

    /// <summary>
    /// Request loading of a terrain chunks.
    /// </summary>
    /// <param name="chunksToLoad">Which chunk types to load, can be All.</param>
    /// <param name="chunkIndex">Index of chunk to load. Can be resolved using TerrainData.GetChunkIndex().</param>
    /// <param name="completionCallback">Called on main thread when loading is completed. The streaming data must *NOT* be retained after the callback has returned as it is owned by the streaming manager and can be pooled.</param>
    /// <param name="callbackData">Optional data to be passed to the callback</param>
    public void Request(ChunksToLoad chunksToLoad, int chunkIndex, StreamingRequestCompletedCallback completionCallback, object? callbackData = null)
    {
        if (!_requestPool.TryPop(out var streamingRequest))
        {
            streamingRequest = new StreamingRequest(_terrain.Header.ChunkTextureSize);
        }

        streamingRequest.ChunksToLoad = chunksToLoad;
        streamingRequest.ChunkIndex = chunkIndex;
        streamingRequest.Callback = completionCallback;
        streamingRequest.CallbackData = callbackData;

        _pendingRequests.Add(streamingRequest);
    }

    private void StreamingThread()
    {
        var cancellationToken = _cts.Token;
        while (true)
        {
            try
            {
                var streamingRequest = _pendingRequests.Take(cancellationToken);
                ProcessStreamingRequest(streamingRequest);

                // Mark for completion.
                _completionQueue.Enqueue(streamingRequest);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ProcessStreamingRequest(StreamingRequest streamingRequest)
    {
        // Read heightmap.
        if ((streamingRequest.ChunksToLoad & ChunksToLoad.Heightmap) == ChunksToLoad.Heightmap)
        {
            _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].HeightmapOffset, SeekOrigin.Begin);
            _stream.ReadAtLeast(streamingRequest.HeightmapData, _terrain.Header.HeightmapSize);
        }

        // Read normal map.
        if ((streamingRequest.ChunksToLoad & ChunksToLoad.NormalMap) == ChunksToLoad.NormalMap)
        {
            _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].NormalMapOffset, SeekOrigin.Begin);
            _stream.ReadAtLeast(streamingRequest.NormalMapData, _terrain.Header.NormalMapSize);
        }

        // Read control map.
        if ((streamingRequest.ChunksToLoad & ChunksToLoad.ControlMap) == ChunksToLoad.ControlMap)
        {
            _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].ControlMapOffset, SeekOrigin.Begin);
            _stream.ReadAtLeast(streamingRequest.ControlMapData, _terrain.Header.ControlMapSize);
        }
    }

    private class StreamingRequest(int chunkTextureSize) : IStreamingRequest
    {
        public readonly byte[] HeightmapData = new byte[chunkTextureSize * chunkTextureSize * sizeof(ushort)]; // r16_unorm
        public readonly byte[] NormalMapData = new byte[chunkTextureSize * chunkTextureSize * 4]; // rgba8
        public readonly byte[] ControlMapData = new byte[chunkTextureSize * chunkTextureSize * sizeof(ushort)]; // r16_uint

        public ChunksToLoad ChunksToLoad { get; set; }
        public int ChunkIndex { get; set; }
        public object? CallbackData;
        public StreamingRequestCompletedCallback? Callback;

        public bool TryGetHeightmap(out Span<byte> data)
        {
            data = HeightmapData;
            return (ChunksToLoad & ChunksToLoad.Heightmap) == ChunksToLoad.Heightmap;
        }

        public bool TryGetNormalMap(out Span<byte> data)
        {
            data = NormalMapData;
            return (ChunksToLoad & ChunksToLoad.NormalMap) == ChunksToLoad.NormalMap;
        }

        public bool TryGetControlMap(out Span<byte> data)
        {
            data = ControlMapData;
            return (ChunksToLoad & ChunksToLoad.ControlMap) == ChunksToLoad.ControlMap;
        }
    }
}
