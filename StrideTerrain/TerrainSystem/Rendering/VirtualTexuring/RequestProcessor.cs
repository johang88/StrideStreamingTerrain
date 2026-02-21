using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public class RequestProcessor
{
    private readonly Dictionary<long, TileState> _tileStates = [];
    private readonly PriorityQueue<TileRequest, float> _workQueue = new();

    public int MaxTilesPerFrame { get; set; } = 8;
    public Vector2 CameraPositionXZ { get; set; }

    /// <summary>
    /// Process feedback requests into a sorted work queue.
    /// </summary>
    public List<TileRequest> ProcessRequests(List<TileRequest> rawRequests)
    {
        // TODO: Don't allocate ...
        if (rawRequests.Count == 0)
            return [];

        _workQueue.Clear();

        foreach (var req in rawRequests)
        {
            long key = TileKey(req.TileX, req.TileY, req.MipLevel);

            // Skip if already resident and valid
            if (_tileStates.TryGetValue(key, out var state) && state == TileState.Resident)
                continue;

            // Skip if already queued for rendering
            if (_tileStates.TryGetValue(key, out state) && state == TileState.Pending)
                continue;

            // Compute priority (lower = higher priority)
            float priority = ComputePriority(req);
            _workQueue.Enqueue(req, priority);
        }

        // Dequeue up to MaxTilesPerFrame
        var tilesToRender = new List<TileRequest>(MaxTilesPerFrame);
        while (tilesToRender.Count < MaxTilesPerFrame && _workQueue.Count > 0)
        {
            var req = _workQueue.Dequeue();
            long key = TileKey(req.TileX, req.TileY, req.MipLevel);
            _tileStates[key] = TileState.Pending;
            tilesToRender.Add(req);
        }

        return tilesToRender;
    }

    /// <summary>
    /// Priority: prefer finer mips, prefer closer to camera.
    /// </summary>
    private float ComputePriority(TileRequest req)
    {
        // Mip priority: finer mips (smaller number) get higher priority
        float mipPriority = req.MipLevel * 100.0f;

        // Distance priority
        float tileWorldSize = VTConstants.BaseTileWorld * MathF.Pow(2, req.MipLevel);
        float tileCenterX = (req.TileX + 0.5f) * tileWorldSize;
        float tileCenterY = (req.TileY + 0.5f) * tileWorldSize;
        float dx = tileCenterX - CameraPositionXZ.X;
        float dy = tileCenterY - CameraPositionXZ.Y;
        float distSq = dx * dx + dy * dy;
        float distPriority = MathF.Sqrt(distSq) * 0.01f;

        return mipPriority + distPriority;
    }

    public void MarkResident(long key) 
        => _tileStates[key] = TileState.Resident;

    public void MarkEvicted(long key) 
        => _tileStates.Remove(key);

    public static long TileKey(int x, int y, int mip) =>
        ((long)mip << 48) | ((long)(y & 0xFFFFFF) << 24) | (long)(x & 0xFFFFFF);

    enum TileState
    {
        NotResident,
        Pending,
        Resident
    }
}

