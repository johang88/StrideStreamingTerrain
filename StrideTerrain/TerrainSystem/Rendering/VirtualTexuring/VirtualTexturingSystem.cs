using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
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
    private const int CT = VTConstants.ClipmapTiles;

    public PhysicalAtlas PhysicalAtlas { get; }
    public TileRenderer TileRenderer { get; }

    // Per-slot bookkeeping: which world tile currently occupies each clipmap position.
    // Initialised to int.MinValue so every slot starts dirty.
    private readonly int[,,] _slotTileX = new int[Mips, CT, CT];
    private readonly int[,,] _slotTileY = new int[Mips, CT, CT];

    // Current clipmap tile origin per mip (top-left corner of the 8×8 window in tile space)
    private readonly Int2[] _currentOrigins = new Int2[Mips];
    private bool _firstUpdate = true;

    // Dirty queue: tiles that need (re-)rendering, sorted ascending by priority (lowest = most urgent)
    private readonly PriorityQueue<TileRequest, float> _dirtyQueue = new();

    // Packed clipmap origins uploaded to the shader each frame.
    // Two mip origins per Vector4 — mip M maps to element [M/2], in .xy (even M) or .zw (odd M).
    public Vector4[] ClipmapOriginsPacked { get; } = new Vector4[5];

    public int MaxTilesPerFrame { get; set; } = 32;

    public VirtualTexturingSystem(IServiceRegistry services, GraphicsDevice graphicsDevice)
    {
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

                // Queue every slot whose expected tile no longer matches what is stored
                for (int cy = 0; cy < CT; cy++)
                {
                    for (int cx = 0; cx < CT; cx++)
                    {
                        int expectedX = newOrigin.X + cx;
                        int expectedY = newOrigin.Y + cy;

                        if (_slotTileX[mip, cy, cx] != expectedX || _slotTileY[mip, cy, cx] != expectedY)
                        {
                            float priority = ComputePriority(expectedX, expectedY, mip, tileWorldSize, cameraXZ);
                            _dirtyQueue.Enqueue(
                                new TileRequest { TileX = expectedX, TileY = expectedY, MipLevel = mip },
                                priority);
                        }
                    }
                }
            }
        }

        _firstUpdate = false;

        // --- Step 2: Pack origins for the shader uniform ---
        PackOrigins();

        // --- Step 3: Drain the dirty queue up to MaxTilesPerFrame ---
        var batch = new List<TileRequest>(MaxTilesPerFrame);

        while (batch.Count < MaxTilesPerFrame && _dirtyQueue.Count > 0)
        {
            var req = _dirtyQueue.Dequeue();

            // Skip stale requests that scrolled out of the current window
            var origin = _currentOrigins[req.MipLevel];
            int relX = req.TileX - origin.X;
            int relY = req.TileY - origin.Y;
            if (relX < 0 || relX >= CT || relY < 0 || relY >= CT)
                continue;

            batch.Add(req);
        }

        if (batch.Count > 0)
        {
            TileRenderer.RenderTiles(context, batch, terrainRuntimeData);

            // Mark rendered slots as current
            foreach (var req in batch)
            {
                var origin = _currentOrigins[req.MipLevel];
                int relX = req.TileX - origin.X;
                int relY = req.TileY - origin.Y;
                if (relX >= 0 && relX < CT && relY >= 0 && relY < CT)
                {
                    _slotTileX[req.MipLevel, relY, relX] = req.TileX;
                    _slotTileY[req.MipLevel, relY, relX] = req.TileY;
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
                ClipmapOriginsPacked[idx].X = origin.X;
                ClipmapOriginsPacked[idx].Y = origin.Y;
            }
            else
            {
                ClipmapOriginsPacked[idx].Z = origin.X;
                ClipmapOriginsPacked[idx].W = origin.Y;
            }
        }
    }

    private static float ComputePriority(int tileX, int tileY, int mip, float tileWorldSize, Vector2 cameraXZ)
    {
        // Lower value = higher priority: fine mips first, then closer to camera
        float tileCenterX = (tileX + 0.5f) * tileWorldSize;
        float tileCenterZ = (tileY + 0.5f) * tileWorldSize;
        float dx = tileCenterX - cameraXZ.X;
        float dz = tileCenterZ - cameraXZ.Y;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        return mip * 10000.0f + dist;
    }

    public void Dispose()
    {
        PhysicalAtlas?.Dispose();
        TileRenderer?.Dispose();
    }
}
