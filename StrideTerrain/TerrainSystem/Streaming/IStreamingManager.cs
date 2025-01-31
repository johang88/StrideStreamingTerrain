using System;

namespace StrideTerrain.TerrainSystem.Streaming;

public delegate void StreamingRequestCompletedCallback(IStreamingRequest streamingRequest, object? callbackData);

public interface IStreamingManager : IDisposable
{
    void ProcessPendingCompletions(int maxCompletionsToProcess = -1);
    void Request(ChunksToLoad chunksToLoad, int chunkIndex, StreamingRequestCompletedCallback completionCallback, object? callbackData = null);
}