using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.TerrainSystem.Rendering;

/// <summary>
/// Manages all mesh related data for the terrain such as the actual mesh and buffers for chunk data.
/// 
/// Optimized for zero per-frame GC allocations.
/// </summary>
public class MeshManager : IDisposable
{
    private readonly TerrainRuntimeData _terrain;
    private readonly GpuTextureManager _gpuTextureManager;

    private readonly ChunkData[] _chunkData;
    private readonly BoundingBoxExt[] _chunkBounds;
    private int _chunkCount;
    private readonly int[] _sectorToChunkMap;

    // Pre-allocated work buffers - reused every frame instead of ArrayPool rent/return
    private readonly int[] _chunksToProcess;
    private readonly int[] _chunksTemp;

    public readonly Buffer ChunkBuffer;
    public readonly Buffer SectorToChunkMapBuffer;
    public readonly Buffer ChunkInstanceDataBuffer;

    // Expose as ReadOnlySpan where mutation isn't needed externally
    public ReadOnlySpan<int> SectorToChunkMap => _sectorToChunkMap;
    public ReadOnlySpan<ChunkData> ChunkData => new ReadOnlySpan<ChunkData>(_chunkData, 0, _chunkCount);

    public bool IsReady => _chunkCount > 0;

    public readonly Mesh Mesh = new()
    {
        Draw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            VertexBuffers = []
        },
        BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
    };

    public MeshManager(TerrainRuntimeData terrain, GraphicsDevice graphicsDevice, GpuTextureManager gpuTextureManager)
    {
        _terrain = terrain;
        _gpuTextureManager = gpuTextureManager;

        var maxChunks = _terrain.ChunksPerRowLod0 * _terrain.ChunksPerRowLod0;

        _chunkData = new ChunkData[maxChunks];
        _chunkBounds = new BoundingBoxExt[maxChunks];
        _sectorToChunkMap = new int[maxChunks];

        // Pre-allocate work buffers once
        _chunksToProcess = new int[maxChunks];
        _chunksTemp = new int[maxChunks];

        ChunkBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, Marshal.SizeOf<ChunkData>(), true);
        SectorToChunkMapBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, sizeof(int), true);
        ChunkInstanceDataBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, sizeof(int), true);

        Mesh.Draw.DrawCount = _terrain.TerrainData.Header.ChunkSize * _terrain.TerrainData.Header.ChunkSize * 6;
    }

    public void Dispose()
    {
        ChunkBuffer.Dispose();
        SectorToChunkMapBuffer.Dispose();
        ChunkInstanceDataBuffer.Dispose();
    }

    public void Update(in Vector3 cameraPosition, ReadOnlySpan<float> lodLevels)
    {
        var terrainData = _terrain.TerrainData;
        var header = terrainData.Header;
        var terrainSize = header.Size;
        var chunkSize = header.ChunkSize;
        var unitsPerTexel = _terrain.UnitsPerTexel;

        var chunksPerRowLod0 = terrainSize / chunkSize;

        var maxLod = header.MaxLod;
        var maxLodSetting = _terrain.MaximumLod >= 0
            ? Math.Min(_terrain.MaximumLod, maxLod)
            : maxLod;
        var minLod = Math.Max(0, _terrain.MinimumLod);

        // Use pre-allocated buffers
        var chunksToProcess = _chunksToProcess;
        var chunksTemp = _chunksTemp;
        var chunkTempCount = 0;
        var chunkCount = 0;

        // Initialize with top-level LOD chunks
        var lod = maxLod;
        var scale = 1 << lod;
        var chunksPerRowCurrentLod = terrainSize / (scale * chunkSize);
        var chunksPerRowNextLod = chunksPerRowCurrentLod << 1;

        // Flatten initial grid population
        var initialCount = chunksPerRowCurrentLod * chunksPerRowCurrentLod;
        for (var i = 0; i < initialCount; i++)
        {
            chunksToProcess[i] = i;
        }
        chunkCount = initialCount;

        _chunkCount = 0;

        // Cache frequently accessed values
        var gpuTextureManager = _gpuTextureManager;
        var chunks = terrainData.Chunks;

        // Process all pending chunks
        while (chunkCount > 0)
        {
            var chunkOffset = chunkSize * scale;
            var halfChunkOffset = chunkOffset * 0.5f;
            var extent = scale * unitsPerTexel * chunkSize * 0.5f;
            var extentDoubled = extent * 2.0f;

            // Calculate LOD distance once per level
            float lodDistance;
            if (lodLevels.Length == 0)
            {
                lodDistance = 50f;
            }
            else if (lod < lodLevels.Length)
            {
                lodDistance = lodLevels[lod];
            }
            else
            {
                lodDistance = lodLevels[^1] * (1 << lod);
            }

            // Pre-calculate camera rect bounds (same for all chunks at this LOD)
            var camRectMinX = cameraPosition.X - lodDistance;
            var camRectMaxX = cameraPosition.X + lodDistance;
            var camRectMinZ = cameraPosition.Z - lodDistance;
            var camRectMaxZ = cameraPosition.Z + lodDistance;

            for (var i = 0; i < chunkCount; i++)
            {
                var chunk = chunksToProcess[i];
                var positionX = chunk % chunksPerRowCurrentLod;
                var positionZ = chunk / chunksPerRowCurrentLod;

                var chunkIndex = terrainData.GetChunkIndex(lod, positionX, positionZ, chunksPerRowCurrentLod);

                // Calculate world position
                var worldX = (positionX * chunkOffset + halfChunkOffset) * unitsPerTexel;
                var worldZ = (positionZ * chunkOffset + halfChunkOffset) * unitsPerTexel;

                // Inline rectangle intersection check (avoid RectangleF allocation/method call)
                var chunkMinX = worldX - extent;
                var chunkMaxX = worldX + extent;
                var chunkMinZ = worldZ - extent;
                var chunkMaxZ = worldZ + extent;

                var shouldSplit = chunkMinX < camRectMaxX && chunkMaxX > camRectMinX &&
                                  chunkMinZ < camRectMaxZ && chunkMaxZ > camRectMinZ;
                shouldSplit &= lod > minLod;
                if (lod > maxLodSetting) shouldSplit = true;

                // If max lod then skip if chunk is not resident yet
                if (lod == maxLod && !gpuTextureManager.RequestChunk(chunkIndex))
                    continue;

                // Check child chunk residency if splitting
                if (shouldSplit)
                {
                    var childBaseX = positionX << 1;
                    var childBaseZ = positionZ << 1;
                    var childLod = lod - 1;

                    // Check all 4 children - use bitwise AND to avoid short-circuit branch mispredictions
                    var child0 = terrainData.GetChunkIndex(childLod, childBaseZ * chunksPerRowNextLod + childBaseX);
                    var child1 = terrainData.GetChunkIndex(childLod, childBaseZ * chunksPerRowNextLod + childBaseX + 1);
                    var child2 = terrainData.GetChunkIndex(childLod, (childBaseZ + 1) * chunksPerRowNextLod + childBaseX);
                    var child3 = terrainData.GetChunkIndex(childLod, (childBaseZ + 1) * chunksPerRowNextLod + childBaseX + 1);

                    var allResident = gpuTextureManager.RequestChunk(child0) &
                                      gpuTextureManager.RequestChunk(child1) &
                                      gpuTextureManager.RequestChunk(child2) &
                                      gpuTextureManager.RequestChunk(child3);
                    shouldSplit = allResident;
                }

                if (shouldSplit && lod > minLod)
                {
                    var childBaseX = positionX << 1;
                    var childBaseZ = positionZ << 1;

                    chunksTemp[chunkTempCount] = childBaseZ * chunksPerRowNextLod + childBaseX;
                    chunksTemp[chunkTempCount + 1] = childBaseZ * chunksPerRowNextLod + childBaseX + 1;
                    chunksTemp[chunkTempCount + 2] = (childBaseZ + 1) * chunksPerRowNextLod + childBaseX;
                    chunksTemp[chunkTempCount + 3] = (childBaseZ + 1) * chunksPerRowNextLod + childBaseX + 1;
                    chunkTempCount += 4;
                }
                else
                {
                    // Map all LOD0 sectors covered by this chunk
                    var ratioToLod0 = chunksPerRowLod0 / chunksPerRowCurrentLod;
                    var offsetX = ratioToLod0 * positionX;
                    var offsetZ = ratioToLod0 * positionZ;
                    var endX = offsetX + ratioToLod0;
                    var endZ = offsetZ + ratioToLod0;

                    // Clamp to valid range
                    var startX = Math.Max(0, offsetX);
                    var startZ = Math.Max(0, offsetZ);
                    endX = Math.Min(chunksPerRowLod0, endX);
                    endZ = Math.Min(chunksPerRowLod0, endZ);

                    var currentChunkIndex = _chunkCount;
                    for (var z = startZ; z < endZ; z++)
                    {
                        var rowOffset = z * chunksPerRowLod0;
                        for (var x = startX; x < endX; x++)
                        {
                            _sectorToChunkMap[rowOffset + x] = currentChunkIndex;
                        }
                    }

                    // Get texture coordinates
                    var textureIndex = gpuTextureManager.GetTextureIndex(chunkIndex);
                    var (tx, ty) = gpuTextureManager.Heightmap!.GetCoordinates(textureIndex);

                    // Build ChunkData with batched bit operations
                    ref var chunkData = ref _chunkData[_chunkCount];
                    chunkData.PackedUv = tx | (ty << 16);
                    chunkData.PackedPositionXZ = (positionX * chunkOffset) | ((positionZ * chunkOffset) << 16);

                    // Pack Data1 (East will be set later, ChunkX, ChunkZ)
                    chunkData.Data1 = (positionX << 8) | (positionZ << 16);

                    // Pack Data0 (LodLevel, North/South/West will be set later)
                    chunkData.Data0 = (byte)lod;

                    // Calculate bounds
                    ref var chunkInfo = ref chunks[chunkIndex];
                    var minHeight = chunkInfo.MinHeight;
                    var maxHeight = chunkInfo.MaxHeight;
                    var halfHeightRange = (maxHeight - minHeight) * 0.5f;

                    _chunkBounds[_chunkCount] = new BoundingBoxExt
                    {
                        Center = new Vector3(worldX, minHeight + halfHeightRange, worldZ),
                        Extent = new Vector3(extent, halfHeightRange, extent)
                    };

                    _chunkCount++;
                }
            }

            // Swap buffers for next iteration (avoid copy)
            chunkCount = chunkTempCount;
            if (chunkTempCount > 0)
            {
                // Copy is unavoidable here, but we minimize it
                System.Buffer.BlockCopy(chunksTemp, 0, chunksToProcess, 0, chunkTempCount * sizeof(int));
            }

            chunksPerRowCurrentLod <<= 1;
            chunksPerRowNextLod <<= 1;
            scale >>= 1;
            lod--;
            chunkTempCount = 0;
        }

        // Calculate LOD differences between chunks
        CalculateLodDifferences(terrainSize, chunkSize, chunksPerRowLod0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CalculateLodDifferences(int terrainSize, int chunkSize, int chunksPerRowLod0)
    {
        for (var i = 0; i < _chunkCount; i++)
        {
            ref var chunk = ref _chunkData[i];
            var lodLevel = chunk.LodLevel;
            var scale = 1 << lodLevel;
            var chunksPerRow = terrainSize / (scale * chunkSize);
            var ratioToLod0 = chunksPerRowLod0 / chunksPerRow;

            var x = chunk.ChunkX;
            var z = chunk.ChunkZ;

            var north = GetLodDifferenceInline(x, z - 1, chunksPerRowLod0, ratioToLod0, lodLevel);
            var south = GetLodDifferenceInline(x, z + 1, chunksPerRowLod0, ratioToLod0, lodLevel);
            var east = GetLodDifferenceInline(x + 1, z, chunksPerRowLod0, ratioToLod0, lodLevel);
            var west = GetLodDifferenceInline(x - 1, z, chunksPerRowLod0, ratioToLod0, lodLevel);

            // Batch update Data0 and Data1 with neighbor LOD differences
            chunk.Data0 = (chunk.Data0 & 0xFF) | (north << 8) | (south << 16) | (west << 24);
            chunk.Data1 = (chunk.Data1 & ~0xFF) | east;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetLodDifferenceInline(int x, int z, int chunksPerRow, int ratioToLod0, int lod)
    {
        x *= ratioToLod0;
        z *= ratioToLod0;

        if ((uint)x >= (uint)chunksPerRow || (uint)z >= (uint)chunksPerRow)
        {
            return 0;
        }

        var chunkIndex = _sectorToChunkMap[z * chunksPerRow + x];
        if (chunkIndex < 0)
            return 0;

        var neighborLod = _chunkData[chunkIndex].LodLevel;
        return (byte)Math.Max(0, neighborLod - lod);
    }

    public void UpdateBuffers(CommandList commandList)
    {
        ChunkBuffer.SetData(commandList, new ReadOnlySpan<ChunkData>(_chunkData, 0, _chunkCount));
        SectorToChunkMapBuffer.SetData(commandList, (ReadOnlySpan<int>)_sectorToChunkMap);
    }

    public int PrepareDraw(CommandList commandList, Matrix viewProjection, Matrix view, bool visiblityIgnoreDepthPlanes = false)
    {
        var frustum = new BoundingFrustum(ref viewProjection);

        var invView = Matrix.Invert(view);
        var cameraPosition = invView.TranslationVector;

        var maxChunks = _terrain.ChunksPerRowLod0 * _terrain.ChunksPerRowLod0;
        var instanceCount = 0;

        // Use pre-allocated buffers from ArrayPool (or could add more permanent buffers to class)
        var chunkInstanceData = ArrayPool<int>.Shared.Rent(maxChunks);
        var distances = ArrayPool<float>.Shared.Rent(maxChunks);

        try
        {
            for (var i = 0; i < _chunkCount; i++)
            {
                ref readonly var bounds = ref _chunkBounds[i];
                if (!VisibilityGroup.FrustumContainsBox(ref frustum, ref Unsafe.AsRef(in bounds), visiblityIgnoreDepthPlanes))
                    continue;

                chunkInstanceData[instanceCount] = i;
                distances[instanceCount] = Vector3.DistanceSquared(cameraPosition, bounds.Center);
                instanceCount++;
            }

            // Sort indices by distance
            var distSpan = distances.AsSpan(0, instanceCount);
            var idxSpan = chunkInstanceData.AsSpan(0, instanceCount);
            distSpan.Sort(idxSpan);

            // Upload to GPU
            ChunkInstanceDataBuffer.SetData(commandList, (ReadOnlySpan<int>)idxSpan);

            return instanceCount;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(chunkInstanceData);
            ArrayPool<float>.Shared.Return(distances);
        }
    }
}