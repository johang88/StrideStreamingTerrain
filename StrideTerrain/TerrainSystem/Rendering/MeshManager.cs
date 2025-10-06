using ServiceWire;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Buffer = Stride.Graphics.Buffer;
namespace StrideTerrain.TerrainSystem.Rendering;

/// <summary>
/// Manages all mesh related data for the terrain such as the actual mesh and buffers for chunk data.
/// 
/// It is also responsible for updating the lod levels relative to the main camera each frame so that they can be consumed by the 
/// render feature as well as issuing streaming requests.
/// </summary>
public class MeshManager : IDisposable
{
    private readonly TerrainRuntimeData _terrain;
    private readonly GpuTextureManager _gpuTextureManager;

    private readonly ChunkData[] _chunkData;
    private readonly BoundingBoxExt[] _chunkBounds;
    private int _chunkCount = 0;
    private readonly int[] _sectorToChunkMap;

    public readonly Buffer ChunkBuffer;
    public readonly Buffer SectorToChunkMapBuffer;
    public readonly Buffer ChunkInstanceDataBuffer;

    public Span<int> SectorToChunkMap => _sectorToChunkMap.AsSpan();
    public Span<ChunkData> ChunkData => _chunkData.AsSpan();

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

        ChunkBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, Marshal.SizeOf<ChunkData>(), true);
        SectorToChunkMapBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, sizeof(int), true);
        ChunkInstanceDataBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, Marshal.SizeOf<int>(), true);

        Mesh.Draw.DrawCount = _terrain.TerrainData.Header.ChunkSize * _terrain.TerrainData.Header.ChunkSize * 6;
    }

    public void Dispose()
    {
        ChunkBuffer.Dispose();
        SectorToChunkMapBuffer.Dispose();
        ChunkInstanceDataBuffer.Dispose();
    }

    public void Update(Vector3 cameraPosition, Span<float> lodLevels)
    {
        var terrainSize = _terrain.TerrainData.Header.Size;
        var chunkSize = _terrain.TerrainData.Header.ChunkSize;

        var chunksPerRowLod0 = terrainSize / chunkSize;
        var maxChunks = chunksPerRowLod0 * chunksPerRowLod0;

        // Setup chunk lod, these are always based on the main camera position, frustum culling of the chunks are done in the render feature
        var maxLod = _terrain.TerrainData.Header.MaxLod; // Max lod = single chunk
        var maxLodSetting = maxLod;
        if (_terrain.MaximumLod >= 0)
            maxLodSetting = Math.Min(_terrain.MaximumLod, maxLodSetting);

        var minLod = Math.Max(0, _terrain.MinimumLod);

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

                var chunkIndex = _terrain.TerrainData.GetChunkIndex(lod, positionX, positionZ, chunksPerRowCurrentLod);

                var chunkWorldPosition = new Vector3(positionX * chunkOffset + (chunkOffset * 0.5f), 0, positionZ * chunkOffset + (chunkOffset * 0.5f)) * _terrain.UnitsPerTexel;

                var extent = scale * _terrain.UnitsPerTexel * chunkSize * 0.5f;
                var maxHeight = _terrain.TerrainData.Header.MaxHeight; // TODO: Should use max height for chunk but there is some issue with it ...
                var heightRange = maxHeight;
                var halfHeightRange = heightRange * 0.5f;

                var bounds = new BoundingBoxExt
                {
                    Center = chunkWorldPosition + new Vector3(0, halfHeightRange, 0),
                    Extent = new(extent, heightRange, extent)
                };

                var lodDistance = lodLevels.Length == 0 ? 50 : (lod < lodLevels.Length ? lodLevels[lod] : (lodLevels[^1] * (1 << lod)));

                var rect = new RectangleF(chunkWorldPosition.X - extent, chunkWorldPosition.Z - extent, extent * 2.0f, extent * 2.0f);
                var cameraRect = new RectangleF(cameraPosition.X - lodDistance, cameraPosition.Z - lodDistance, lodDistance * 2.0f, lodDistance * 2.0f);

                // Split if desired, otherwise add instance for current lod level
                cameraRect.Intersects(ref rect, out var shouldSplit);
                shouldSplit &= lod > minLod;
                if (lod > maxLodSetting) shouldSplit = true;

                // If max lod then skip if chunk is not resident yet
                // Cannot happen for other lod's as the chunk wont be split if child chunks are not resident.
                if (lod == maxLod && !_gpuTextureManager!.RequestChunk(_terrain.TerrainData.GetChunkIndex(lod, positionX, positionZ, chunksPerRowCurrentLod)))
                    continue;

                // Request streaming if desired, chunk will only be split into next lod if all children are resident.
                if (shouldSplit)
                {
                    if (!_gpuTextureManager!.RequestChunk(_terrain.TerrainData.GetChunkIndex(lod - 1, positionZ * 2 * chunksPerRowNextLod + (positionX * 2)))) shouldSplit = false;
                    if (!_gpuTextureManager!.RequestChunk(_terrain.TerrainData.GetChunkIndex(lod - 1, positionZ * 2 * chunksPerRowNextLod + (positionX * 2 + 1)))) shouldSplit = false;
                    if (!_gpuTextureManager!.RequestChunk(_terrain.TerrainData.GetChunkIndex(lod - 1, (positionZ * 2 + 1) * chunksPerRowNextLod + (positionX * 2)))) shouldSplit = false;
                    if (!_gpuTextureManager!.RequestChunk(_terrain.TerrainData.GetChunkIndex(lod - 1, (positionZ * 2 + 1) * chunksPerRowNextLod + (positionX * 2 + 1)))) shouldSplit = false;
                }

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

                    var textureIndex = _gpuTextureManager!.GetTextureIndex(chunkIndex);

                    var (tx, ty) = _gpuTextureManager!.Heightmap!.GetCoordinates(textureIndex);

                    _chunkData[_chunkCount].UvX = (ushort)tx;
                    _chunkData[_chunkCount].UvY = (ushort)ty;

                    _chunkData[_chunkCount].LodLevel = (byte)lod;
                    _chunkData[_chunkCount].ChunkX = (byte)positionX;
                    _chunkData[_chunkCount].ChunkZ = (byte)positionZ;
                    _chunkData[_chunkCount].PositionX = (ushort)(positionX * chunkOffset);
                    _chunkData[_chunkCount].PositionZ = (ushort)(positionZ * chunkOffset);
                    _chunkBounds[_chunkCount] = bounds;
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

    public void PrepareDraw(CommandList commandList, RenderMesh renderMesh, BoundingFrustum frustum, bool VisiblityIgnoreDepthPlanes)
    {
        var maxChunks = _terrain.ChunksPerRowLod0 * _terrain.ChunksPerRowLod0;
        renderMesh.InstanceCount = 0;
        var chunkInstanceData = ArrayPool<int>.Shared.Rent(maxChunks);
        for (var i = 0; i < _chunkCount; i++)
        {
            if (!VisibilityGroup.FrustumContainsBox(ref frustum, ref _chunkBounds[i], VisiblityIgnoreDepthPlanes))
                continue;

            chunkInstanceData[renderMesh.InstanceCount++] = i;
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
