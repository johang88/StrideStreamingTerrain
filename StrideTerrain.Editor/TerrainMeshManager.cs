using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.TerrainSystem.Rendering;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.Editor;

/// <summary>
/// Basically a copy of the streaming mesh manager
/// </summary>
public class TerrainMeshManager
{
    private const int MaxSize = 8192 * 2;
    public const int ChunkSize = 64;
    private const int MaxChunksPerRowLod0 = MaxSize / ChunkSize;

    private readonly ChunkData[] _chunkData = [];
    private readonly int[] _sectorToChunkMap = [];
    private int _chunkCount;

    public readonly Buffer ChunkBuffer;
    public readonly Buffer SectorToChunkMapBuffer;
    public readonly Buffer ChunkInstanceDataBuffer;

    public readonly Mesh Mesh = new()
    {
        Draw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            VertexBuffers = []
        },
        BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
    };

    public TerrainMeshManager(GraphicsDevice graphicsDevice)
    {
        // Conservative :D 
        var maxChunks = MaxChunksPerRowLod0 * MaxChunksPerRowLod0;

        _chunkData = new ChunkData[maxChunks];
        _sectorToChunkMap = new int[maxChunks];

        ChunkBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, Marshal.SizeOf<ChunkData>(), true);
        SectorToChunkMapBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, sizeof(int), true);
        ChunkInstanceDataBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, Marshal.SizeOf<int>(), true);

        Mesh.Draw.DrawCount = ChunkSize * ChunkSize * 6;
    }

    public void Update(TerrainSettings terrain,  Vector3 cameraPosition)
    {
        var terrainSize = terrain.Resolution;
        var chunkSize = ChunkSize;

        var chunksPerRowLod0 = terrainSize / chunkSize;
        var maxChunks = chunksPerRowLod0 * chunksPerRowLod0;

        // Setup chunk lod, these are always based on the main camera position, frustum culling of the chunks are done in the render feature
        var maxLod = (int)Math.Log2(terrainSize / chunkSize);
        var minLod = 0;

        var chunksToProcess = ArrayPool<int>.Shared.Rent(maxChunks);
        var chunksTemp = ArrayPool<int>.Shared.Rent(maxChunks);
        var chunkTempCount = 0;

        var chunkCount = 0;

        var lod = maxLod;
        var scale = 1 << lod;
        var chunksPerRowCurrentLod = terrainSize / (scale * chunkSize);
        var chunksPerRowNextLod = chunksPerRowCurrentLod * 2;
        for (var y = 0; y < chunksPerRowCurrentLod; y++)
        {
            for (var x = 0; x < chunksPerRowCurrentLod; x++)
            {
                chunksToProcess[chunkCount++] = y * chunksPerRowCurrentLod + x;
            }
        }

        _chunkCount = 0;

        // Process all pending chunks
        while (chunkCount > 0)
        {
            for (var i = 0; i < chunkCount; i++)
            {
                var chunk = chunksToProcess[i];

                var positionX = chunk % chunksPerRowCurrentLod;
                var positionZ = chunk / chunksPerRowCurrentLod;

                scale = 1 << lod;
                var chunkOffset = chunkSize * scale;

                var chunkWorldPosition = new Vector3(positionX * chunkOffset + (chunkOffset * 0.5f), 0, positionZ * chunkOffset + (chunkOffset * 0.5f)) * terrain.UnitsPerTexel;

                var extent = scale * terrain.UnitsPerTexel * chunkSize * 0.5f;
                var maxHeight = terrain.MaxHeight;
                var heightRange = maxHeight;
                var halfHeightRange = heightRange * 0.5f;

                var bounds = new BoundingBoxExt
                {
                    Center = chunkWorldPosition + new Vector3(0, halfHeightRange, 0),
                    Extent = new(extent, heightRange, extent)
                };

                var lodDistance = 64 * (1 << lod);

                var rect = new RectangleF(chunkWorldPosition.X - extent, chunkWorldPosition.Z - extent, extent * 2.0f, extent * 2.0f);
                var cameraRect = new RectangleF(cameraPosition.X - lodDistance, cameraPosition.Z - lodDistance, lodDistance * 2.0f, lodDistance * 2.0f);

                // Split if desired, otherwise add instance for current lod level
                cameraRect.Intersects(ref rect, out var shouldSplit);
                shouldSplit &= lod > minLod;

                if (shouldSplit && lod > minLod)
                {
                    chunksTemp[chunkTempCount++] = positionZ * 2 * chunksPerRowNextLod + (positionX * 2);
                    chunksTemp[chunkTempCount++] = positionZ * 2 * chunksPerRowNextLod + (positionX * 2 + 1);
                    chunksTemp[chunkTempCount++] = (positionZ * 2 + 1) * chunksPerRowNextLod + (positionX * 2);
                    chunksTemp[chunkTempCount++] = (positionZ * 2 + 1) * chunksPerRowNextLod + (positionX * 2 + 1);
                }
                else
                {
                    var ratioToLod0 = chunksPerRowLod0 / chunksPerRowCurrentLod;
                    var offsetX = ratioToLod0 * positionX;
                    var offsetZ = ratioToLod0 * positionZ;
                    var w = offsetX + ratioToLod0;
                    var h = offsetZ + ratioToLod0;
                    for (var z = offsetZ; z < h; z++)
                    {
                        for (var x = offsetX; x < w; x++)
                        {
                            if (z < 0 || x < 0 || z >= chunksPerRowLod0 || x > chunksPerRowLod0)
                                continue;

                            var index = z * chunksPerRowLod0 + x;
                            _sectorToChunkMap[index] = _chunkCount;
                        }
                    }

                    _chunkData[_chunkCount].LodLevel = (byte)lod;
                    _chunkData[_chunkCount].ChunkX = (byte)positionX;
                    _chunkData[_chunkCount].ChunkZ = (byte)positionZ;
                    _chunkData[_chunkCount].PositionX = (ushort)(positionX * chunkOffset);
                    _chunkData[_chunkCount].PositionZ = (ushort)(positionZ * chunkOffset);
                    _chunkCount++;
                }
            }

            // Copy pending chunks for processing
            chunkCount = 0;
            for (var i = 0; i < chunkTempCount; i++)
            {
                chunksToProcess[i] = chunksTemp[i];
                chunkCount++;
            }

            chunksPerRowCurrentLod *= 2;
            chunksPerRowNextLod *= 2;
            lod--;

            chunkTempCount = 0;
        }

        ArrayPool<int>.Shared.Return(chunksToProcess);
        ArrayPool<int>.Shared.Return(chunksTemp);

        // Calculate lod differences between chunks
        for (var i = 0; i < _chunkCount; i++)
        {
            ref var chunk = ref _chunkData[i];
            scale = 1 << chunk.LodLevel;
            var chunksPerRow = terrainSize / (scale * chunkSize);

            var x = chunk.ChunkX;
            var z = chunk.ChunkZ;

            var ratioToLod0 = chunksPerRowLod0 / chunksPerRow;

            chunk.North = GetLodDifference(x, z - 1, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
            chunk.South = GetLodDifference(x, z + 1, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
            chunk.East = GetLodDifference(x + 1, z, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
            chunk.West = GetLodDifference(x - 1, z, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
        }
    }

    public void UpdateBuffers(CommandList commandList)
    {
        ChunkBuffer.SetData(commandList, (ReadOnlySpan<ChunkData>)_chunkData.AsSpan(0, _chunkCount));
        SectorToChunkMapBuffer.SetData(commandList, (ReadOnlySpan<int>)_sectorToChunkMap.AsSpan());
    }

    public void PrepareDraw(CommandList commandList, RenderMesh renderMesh, RenderView renderView)
    {
        var frustum = new BoundingFrustum(ref renderView.ViewProjection);

        var invView = Matrix.Invert(renderView.View);
        var cameraPosition = invView.TranslationVector;

        // Frustum cull instances
        var maxChunks = MaxChunksPerRowLod0 * MaxChunksPerRowLod0;
        renderMesh.InstanceCount = 0;
        var chunkInstanceData = ArrayPool<int>.Shared.Rent(maxChunks);
        for (var i = 0; i < _chunkCount; i++)
        {
            // TODO: This is useless?
            chunkInstanceData[renderMesh.InstanceCount] = i;
            renderMesh.InstanceCount++;
        }

        // Upload to GPU.
        ChunkInstanceDataBuffer.SetData(commandList, (ReadOnlySpan<int>)chunkInstanceData.AsSpan(0, renderMesh.InstanceCount));

        ArrayPool<int>.Shared.Return(chunkInstanceData);
    }

    byte GetLodDifference(int x, int z, int chunksPerRow, int ratioToLod0, int lod)
    {
        x *= ratioToLod0;
        z *= ratioToLod0;

        if (x < 0 || z < 0 || x >= chunksPerRow || z >= chunksPerRow)
        {
            return 0;
        }
        else
        {
            var chunkIndex = _sectorToChunkMap[z * chunksPerRow + x];
            if (chunkIndex == -1)
                return 0;
            return (byte)Math.Max(0, _chunkData[chunkIndex].LodLevel - lod);
        }
    }
}
