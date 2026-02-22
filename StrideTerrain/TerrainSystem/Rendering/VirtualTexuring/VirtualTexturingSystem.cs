using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Common;
using System;
using System.Collections.Generic;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

/// <summary>
/// Manages a toroidal clipmap virtual texturing system.
/// Each mip level maintains a fixed ClipmapTiles×ClipmapTiles window centred on the camera.
/// Physical slots are assigned deterministically — no indirection texture, no LRU.
/// Tiles scrolling into view are queued for re-rendering; everything out of range returns gray.
/// </summary>
public class VirtualTexturingSystem : IDisposable
{
    private const int Mips = VTConstants.MipCount;
    private const int CT   = VTConstants.ClipmapTiles;

    public PhysicalAtlas PhysicalAtlas { get; }
    public TileRenderer TileRenderer { get; }

    // Per-slot bookkeeping: which world tile currently occupies each clipmap position.
    // Initialised to int.MinValue so every slot starts dirty.
    private readonly int[,,] _slotTileX = new int[Mips, CT, CT];
    private readonly int[,,] _slotTileY = new int[Mips, CT, CT];

    // Current clipmap tile origin per mip (top-left corner of the CT×CT window in tile space)
    private readonly Int2[] _currentOrigins = new Int2[Mips];
    private bool _firstUpdate = true;

    // One dirty queue per mip level. Each queue is sorted by distance (closest first).
    // Keeping queues separate lets us drain a few tiles from every mip every frame so that
    // all mips build up coverage simultaneously — avoiding the sharp coarse-right-after-fine
    // band that appears when a single queue drains mips serially (coarse-first or fine-first).
    private readonly PriorityQueue<TileRequest, float>[] _dirtyQueues = new PriorityQueue<TileRequest, float>[Mips];

    // Pre-allocated batch buffer — reused every frame to avoid per-frame heap allocations.
    private readonly List<TileRequest> _batch = new(64);

    // Packed clipmap origins uploaded to the shader as 5 individual uniforms.
    // Two mip origins per Vector4 — Packed0.xy=mip0, .zw=mip1 | Packed1.xy=mip2, .zw=mip3 | ...
    private readonly Vector4[] _originsPacked = new Vector4[5];
    public Vector4 ClipmapOriginsPacked0 => _originsPacked[0];
    public Vector4 ClipmapOriginsPacked1 => _originsPacked[1];
    public Vector4 ClipmapOriginsPacked2 => _originsPacked[2];
    public Vector4 ClipmapOriginsPacked3 => _originsPacked[3];
    public Vector4 ClipmapOriginsPacked4 => _originsPacked[4];

    public int MaxTilesPerFrame { get; set; } = 64;

    public VirtualTexturingSystem(IServiceRegistry services, GraphicsDevice graphicsDevice)
    {
        for (int i = 0; i < Mips; i++)
            _dirtyQueues[i] = new PriorityQueue<TileRequest, float>();

        PhysicalAtlas = new(graphicsDevice);
        TileRenderer = new(services, graphicsDevice, PhysicalAtlas);

        // Poison slot tracking so every slot is considered dirty on the first update
        for (int m = 0; m < Mips; m++)
            for (int cy = 0; cy < CT; cy++)
                for (int cx = 0; cx < CT; cx++)
                {
                    _slotTileX[m, cy, cx] = int.MinValue;
                    _slotTileY[m, cy, cx] = int.MinValue;
                }
    }

    /// <summary>
    /// Poisons every slot and marks the system as requiring a full re-render.
    /// All tiles will be re-queued on the next <see cref="Update"/> call.
    /// </summary>
    public void InvalidateAll()
    {
        for (int m = 0; m < Mips; m++)
            for (int cy = 0; cy < CT; cy++)
                for (int cx = 0; cx < CT; cx++)
                {
                    _slotTileX[m, cy, cx] = int.MinValue;
                    _slotTileY[m, cy, cx] = int.MinValue;
                }
        _firstUpdate = true;
    }

    public void Update(RenderDrawContext context, Vector3 cameraWorldPosition, TerrainRuntimeData terrainRuntimeData)
    {
        var cameraXZ = new Vector2(cameraWorldPosition.X, cameraWorldPosition.Z);

        // --- Step 1: Compute new clipmap origins and enqueue newly exposed tiles ---
        for (int mip = 0; mip < Mips; mip++)
        {
            float tileWorldSize = VTConstants.BaseTileWorld * MathF.Pow(2, mip);
            int camTileX = (int)Math.Floor((double)(cameraXZ.X / tileWorldSize));
            int camTileY = (int)Math.Floor((double)(cameraXZ.Y / tileWorldSize));

            var newOrigin = new Int2(camTileX - CT / 2, camTileY - CT / 2);
            bool originChanged = _firstUpdate || newOrigin != _currentOrigins[mip];

            if (originChanged)
            {
                _currentOrigins[mip] = newOrigin;

                // Queue every slot whose expected tile no longer matches what is stored.
                // IMPORTANT: index _slotTileX/Y by toroidal coords, not origin-relative coords.
                // Using origin-relative indices would fail to recognise tiles that are already in
                // the correct toroidal slot after the origin scrolls, causing spurious re-renders.
                for (int cy = 0; cy < CT; cy++)
                {
                    for (int cx = 0; cx < CT; cx++)
                    {
                        int expectedX = newOrigin.X + cx;
                        int expectedY = newOrigin.Y + cy;

                        int toroX = ((expectedX % CT) + CT) % CT;
                        int toroY = ((expectedY % CT) + CT) % CT;

                        if (_slotTileX[mip, toroY, toroX] != expectedX || _slotTileY[mip, toroY, toroX] != expectedY)
                        {
                            float priority = ComputeDistancePriority(expectedX, expectedY, tileWorldSize, cameraXZ);
                            _dirtyQueues[mip].Enqueue(
                                new TileRequest { TileX = expectedX, TileY = expectedY, MipLevel = mip },
                                priority);
                        }
                    }
                }
            }
        }

        _firstUpdate = false;

        // --- Step 1b: Invalidate tiles whose source terrain data just became resident ---
        // ProcessPendingCompletions() runs between GpuTextureManager.Update() and here, so
        // NewlyResidentChunks is already populated for this frame when we reach this point.
        // We poison the slot and directly enqueue the tile so it is re-rendered this same frame
        // rather than waiting until the camera moves and Step 1 detects the mismatch.
        //var gpuManager = terrainRuntimeData.GpuTextureManager;
        //if (gpuManager != null && gpuManager.NewlyResidentChunks.Count > 0)
        //{
        //    foreach (var chunkIdx in gpuManager.NewlyResidentChunks)
        //    {
        //        var (minX, maxX, minZ, maxZ) = terrainRuntimeData.TerrainData.GetChunkWorldBounds(chunkIdx);

        //        // Poison and immediately re-enqueue every VT tile that overlaps this region.
        //        for (int mip = 0; mip < Mips; mip++)
        //        {
        //            float tileWorldSize = VTConstants.BaseTileWorld * MathF.Pow(2, mip);
        //            int tileMinX = (int)Math.Floor(minX / tileWorldSize);
        //            int tileMinZ = (int)Math.Floor(minZ / tileWorldSize);
        //            int tileMaxX = (int)Math.Ceiling(maxX / tileWorldSize);
        //            int tileMaxZ = (int)Math.Ceiling(maxZ / tileWorldSize);

        //            var origin = _currentOrigins[mip];
        //            for (int tz = tileMinZ; tz < tileMaxZ; tz++)
        //            {
        //                for (int tx = tileMinX; tx < tileMaxX; tx++)
        //                {
        //                    int relX = tx - origin.X;
        //                    int relZ = tz - origin.Y;
        //                    if (relX < 0 || relX >= CT || relZ < 0 || relZ >= CT)
        //                        continue;

        //                    int toroX = ((tx % CT) + CT) % CT;
        //                    int toroZ = ((tz % CT) + CT) % CT;
        //                    _slotTileX[mip, toroZ, toroX] = int.MinValue;
        //                    _slotTileY[mip, toroZ, toroX] = int.MinValue;

        //                    float priority = ComputeDistancePriority(tx, tz, tileWorldSize, cameraXZ);
        //                    _dirtyQueues[mip].Enqueue(
        //                        new TileRequest { TileX = tx, TileY = tz, MipLevel = mip },
        //                        priority);
        //                }
        //            }
        //        }
        //    }
        //}

        // --- Step 2: Pack origins for the shader uniform ---
        PackOrigins();

        // --- Step 3: Drain per-mip queues, giving each mip an equal share of the frame budget ---
        // This ensures all mips build coverage simultaneously so the shader always finds a valid
        // tile at (or near) the correct mip level. A single merged queue would drain mips serially,
        // leaving intermediate mips unrendered and causing hard coarse/fine boundaries in the fallback.
        _batch.Clear();
        int budgetPerMip = Math.Max(1, MaxTilesPerFrame / Mips);

        for (int mip = 0; mip < Mips && _batch.Count < MaxTilesPerFrame; mip++)
        {
            int thisMipBudget = Math.Min(budgetPerMip, MaxTilesPerFrame - _batch.Count);
            int rendered = 0;

            while (rendered < thisMipBudget && _dirtyQueues[mip].Count > 0)
            {
                var req = _dirtyQueues[mip].Dequeue();

                // Skip stale requests that scrolled out of the current window
                var origin = _currentOrigins[mip];
                int relX = req.TileX - origin.X;
                int relY = req.TileY - origin.Y;
                if (relX < 0 || relX >= CT || relY < 0 || relY >= CT)
                    continue;

                _batch.Add(req);
                rendered++;
            }
        }

        // Give any unused budget to whichever mip still has pending tiles
        if (_batch.Count < MaxTilesPerFrame)
        {
            for (int mip = 0; mip < Mips && _batch.Count < MaxTilesPerFrame; mip++)
            {
                while (_batch.Count < MaxTilesPerFrame && _dirtyQueues[mip].Count > 0)
                {
                    var req = _dirtyQueues[mip].Dequeue();
                    var origin = _currentOrigins[mip];
                    int relX = req.TileX - origin.X;
                    int relY = req.TileY - origin.Y;
                    if (relX < 0 || relX >= CT || relY < 0 || relY >= CT)
                        continue;
                    _batch.Add(req);
                }
            }
        }

        if (_batch.Count > 0)
        {
            TileRenderer.RenderTiles(context, _batch, terrainRuntimeData);

            // Mark rendered slots as current (use toroidal indices to match dirty check)
            foreach (var req in _batch)
            {
                var origin = _currentOrigins[req.MipLevel];
                int relX = req.TileX - origin.X;
                int relY = req.TileY - origin.Y;
                if (relX >= 0 && relX < CT && relY >= 0 && relY < CT)
                {
                    int toroX = ((req.TileX % CT) + CT) % CT;
                    int toroY = ((req.TileY % CT) + CT) % CT;
                    _slotTileX[req.MipLevel, toroY, toroX] = req.TileX;
                    _slotTileY[req.MipLevel, toroY, toroX] = req.TileY;
                }
            }
        }
    }

    private void PackOrigins()
    {
        for (int mip = 0; mip < Mips; mip++)
        {
            int idx = mip / 2;
            var origin = _currentOrigins[mip];

            if ((mip & 1) == 0)
            {
                _originsPacked[idx].X = origin.X;
                _originsPacked[idx].Y = origin.Y;
            }
            else
            {
                _originsPacked[idx].Z = origin.X;
                _originsPacked[idx].W = origin.Y;
            }
        }
    }

    // Within a per-mip queue, tiles closer to the camera are rendered first.
    private static float ComputeDistancePriority(int tileX, int tileY, float tileWorldSize, Vector2 cameraXZ)
    {
        float tileCenterX = (tileX + 0.5f) * tileWorldSize;
        float tileCenterZ = (tileY + 0.5f) * tileWorldSize;
        float dx = tileCenterX - cameraXZ.X;
        float dz = tileCenterZ - cameraXZ.Y;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public void Dispose()
    {
        PhysicalAtlas?.Dispose();
        TileRenderer?.Dispose();
    }
}
