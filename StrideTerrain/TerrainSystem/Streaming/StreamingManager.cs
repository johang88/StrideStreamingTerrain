using StrideTerrain.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

    public int PendingStreamingRequests => _pendingRequests.Count;
    public int PendingCompletions => _completionQueue.Count;

    private readonly Stopwatch _timer = new();

    public StreamingManager(TerrainData terrain, ITerrainDataProvider terrainDataProvider)
    {
        _terrain = terrain;

        var (stream, baseOffset) = terrainDataProvider.OpenStreamingData();
        _stream = stream;
        _baseOffset = baseOffset;
        _timer.Start();
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
    /// <param name="maxTimeMilliseconds">Maximum time for processing in ms. Set to -1 for unlimited.</param>
    public void ProcessPendingCompletions(int maxTimeMilliseconds = -1)
    {
        if (_ioThread == null)
        {
            _ioThread = new Thread(StreamingThread);
            _ioThread.Start();
        }

        var now = _timer.ElapsedMilliseconds;
        while ((_timer.ElapsedMilliseconds - now) < maxTimeMilliseconds || maxTimeMilliseconds == -1)
        {
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
            streamingRequest = new StreamingRequest(_terrain.Header.ChunkTextureSize, _terrain.Header.NormalMapTextureSize);
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
            streamingRequest.HeightmapLength = _terrain.Header.HeightmapSize;
            _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].HeightmapOffset, SeekOrigin.Begin);
            _stream.ReadAtLeast(streamingRequest.HeightmapData, _terrain.Header.HeightmapSize);
        }

        // Read normal map.
        if ((streamingRequest.ChunksToLoad & ChunksToLoad.NormalMap) == ChunksToLoad.NormalMap)
        {
            streamingRequest.NormalMapLength = _terrain.Chunks[streamingRequest.ChunkIndex].NormalMapSize;
            _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].NormalMapOffset, SeekOrigin.Begin);
            _stream.ReadAtLeast(streamingRequest.NormalMapData, _terrain.Chunks[streamingRequest.ChunkIndex].NormalMapSize);
        }

        // Read control map.
        if ((streamingRequest.ChunksToLoad & ChunksToLoad.ControlMap) == ChunksToLoad.ControlMap)
        {
            streamingRequest.ControlMapLength = _terrain.Header.ControlMapSize;
            _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].ControlMapOffset, SeekOrigin.Begin);
            _stream.ReadAtLeast(streamingRequest.ControlMapData, _terrain.Header.ControlMapSize);
        }
    }

    private class StreamingRequest(int chunkTextureSize, int normalMapTextureSize) : IStreamingRequest
    {
        public readonly byte[] HeightmapData = new byte[chunkTextureSize * chunkTextureSize * sizeof(ushort)]; // r16_unorm
        public readonly byte[] NormalMapData = new byte[normalMapTextureSize * normalMapTextureSize * 2]; // rg8_unrom (or bc5)
        public readonly byte[] ControlMapData = new byte[chunkTextureSize * chunkTextureSize * sizeof(ushort)]; // r16_uint

        public int HeightmapLength;
        public int NormalMapLength;
        public int ControlMapLength;

        public ChunksToLoad ChunksToLoad { get; set; }
        public int ChunkIndex { get; set; }
        public object? CallbackData;
        public StreamingRequestCompletedCallback? Callback;

        public bool TryGetHeightmap(out Span<byte> data)
        {
            data = HeightmapData.AsSpan(0, HeightmapLength);
            return (ChunksToLoad & ChunksToLoad.Heightmap) == ChunksToLoad.Heightmap;
        }

        public bool TryGetNormalMap(out Span<byte> data)
        {
            data = NormalMapData.AsSpan(0, NormalMapLength);
            return (ChunksToLoad & ChunksToLoad.NormalMap) == ChunksToLoad.NormalMap;
        }

        public bool TryGetControlMap(out Span<byte> data)
        {
            data = ControlMapData.AsSpan(0, ControlMapLength);
            return (ChunksToLoad & ChunksToLoad.ControlMap) == ChunksToLoad.ControlMap;
        }
    }
}
