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
/// 
/// It might make sense to switch to the inbuilt streaming system in Stride after/if a custom asset type
/// is implemented for the terrain. The inbuilt streaming system does not support files sizes above
/// int.MaxValue and neither does virtual file system stream.
/// </summary>
public sealed class StreamingManager : IDisposable
{
    public delegate void StreamingRequestCompletedCallback(IStreamingRequest streamingRequest, object? callbackData);

    private Thread? _ioThread;
    private readonly CancellationTokenSource _cts = new();

    private readonly TerrainData _terrain;
    private readonly Stream _stream;
    private readonly long _baseOffset;

    private BlockingCollection<StreamingRequest> _streamingRequests = [];
    private ConcurrentQueue<StreamingRequest> _completedStreamingRequests = [];
    private Stack<StreamingRequest> _streamingRequestPool = new(256);

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
    /// Should be called each frame on the main thread.
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

            if (!_completedStreamingRequests.TryDequeue(out var streamingRequest))
                break;

            streamingRequest.Callback?.Invoke(streamingRequest, streamingRequest.CallbackData);
            _streamingRequestPool.Push(streamingRequest);
        }
    }

    /// <summary>
    /// Request loading of a terrain chunks.
    /// </summary>
    /// <param name="partsToLoad">Which parts to load, can be All.</param>
    /// <param name="chunkIndex">Index of chunk to load. Can be resolved using TerrainData.GetChunkIndex().</param>
    /// <param name="completionCallback">Called on main thread when loading is completed. The streaming data must *NOT* be retained after the callback has returned as it is owned by the streaming manager and can be pooled.</param>
    /// <param name="callbackData">Optional data to be passed to the callback</param>
    public void Request(PartsToLoad partsToLoad, int chunkIndex, StreamingRequestCompletedCallback completionCallback, object? callbackData = null)
    {
        if (!_streamingRequestPool.TryPop(out var streamingRequest))
        {
            streamingRequest = new StreamingRequest(_terrain.Header.ChunkTextureSize);
        }

        streamingRequest.PartsToLoad = partsToLoad;
        streamingRequest.ChunkIndex = chunkIndex;
        streamingRequest.Callback = completionCallback;
        streamingRequest.CallbackData = callbackData;

        _streamingRequests.Add(streamingRequest);
    }

    private void StreamingThread()
    {
        var cancellationToken = _cts.Token;
        while (true)
        {
            try
            {
                var streamingRequest = _streamingRequests.Take(cancellationToken);

                // Read heightmap.
                if ((streamingRequest.PartsToLoad & PartsToLoad.Heightmap) == PartsToLoad.Heightmap)
                {
                    _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].HeightmapOffset, SeekOrigin.Begin);
                    _stream.ReadAtLeast(streamingRequest.HeightmapData, _terrain.Header.HeightmapSize);
                }

                // Read normal map.
                if ((streamingRequest.PartsToLoad & PartsToLoad.NormalMap) == PartsToLoad.NormalMap)
                {
                    _stream.Seek(_baseOffset + _terrain.Chunks[streamingRequest.ChunkIndex].NormalMapOffset, SeekOrigin.Begin);
                    _stream.ReadAtLeast(streamingRequest.NormalMapData, _terrain.Header.NormalMapSize);
                }

                // Mark for completion.
                _completedStreamingRequests.Enqueue(streamingRequest);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private class StreamingRequest(int chunkTextureSize) : IStreamingRequest
    {
        public readonly byte[] HeightmapData = new byte[chunkTextureSize * chunkTextureSize * sizeof(ushort)]; // r16_unorm
        public readonly byte[] NormalMapData = new byte[chunkTextureSize * chunkTextureSize * 4]; // rgba8

        public PartsToLoad PartsToLoad { get; set; }
        public int ChunkIndex { get; set; }
        public object? CallbackData;
        public StreamingRequestCompletedCallback? Callback;

        public bool TryGetHeightmap(out ReadOnlySpan<byte> data)
        {
            data = HeightmapData;
            return (PartsToLoad & PartsToLoad.Heightmap) == PartsToLoad.Heightmap;
        }

        public bool TryGetNormalMap(out ReadOnlySpan<byte> data)
        {
            data = NormalMapData;
            return (PartsToLoad & PartsToLoad.NormalMap) == PartsToLoad.NormalMap;
        }
    }
}
