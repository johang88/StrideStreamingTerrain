using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Profiling;
using Stride.Rendering;
using StrideTerrain.TerrainSystem.Rendering;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.TerrainSystem;

public class TerrainProcessor : EntityProcessor<TerrainComponent, TerrainRuntimeData>, IEntityComponentRenderProcessor
{
    private static readonly ProfilingKey ProfilingKeyUpdate = new("Terrain.Update");
    private static readonly ProfilingKey ProfilingKeyChunk = new("Terrain.Chunk");

    private readonly Dictionary<RenderModel, TerrainRuntimeData> _modelToTerrainMap = [];

    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override TerrainRuntimeData GenerateComponentData([NotNull] Entity entity, [NotNull] TerrainComponent component)
        => new();

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] TerrainComponent component, [NotNull] TerrainRuntimeData data)
    {
        base.OnEntityComponentRemoved(entity, component, data);

        if (data.RenderModel != null)
            _modelToTerrainMap.Remove(data.RenderModel);

        data.Dispose();
    }

    public override void Update(GameTime time)
    {
        base.Update(time);

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>().GraphicsDevice;
        var graphicsContext = Services.GetService<GraphicsContext>();
        var contentManager = Services.GetService<ContentManager>();

        using var profilingScope = Profiler.Begin(ProfilingKeyUpdate);

        foreach (var pair in ComponentDatas)
        {
            var component = pair.Key;
            var data = pair.Value;

            if (component.Material == null)
            {
                data.IsInitialized = false;
                continue;
            }

            if (component.TerrainData == null || component.TerrainStreamingData == null)
            {
                data.IsInitialized = false;
                continue;
            }

            // Sync component settings
            data.UnitsPerTexel = component.UnitsPerTexel;
            data.Lod0Distance = component.Lod0Distance;
            data.MaximumLod = component.MaximumLod;
            data.MinimumLod = component.MinimumLod;

            // Setup model
            var entity = component.Entity;
            data.ModelComponent = entity.GetOrCreate<ModelComponent>();

            if (data.ModelComponent.Model == null)
            {
                data.ModelComponent.Model ??= [data.Mesh];
                data.ModelComponent.Model.BoundingSphere = new(Vector3.Zero, 10000);
                data.ModelComponent.Model.BoundingBox = BoundingBox.FromSphere(data.ModelComponent.BoundingSphere);
                data.ModelComponent.IsShadowCaster = false;
                data.ModelComponent.Materials[0] = component.Material;
                data.ModelComponent.Enabled = false; // Stays disabled until everything is ready.
            }

            // Load initial data.
            if (data.TerrainDataUrl != component.TerrainData.Url)
            {
#if GAME_EDITOR
                data.DataProvider = new EditorTerrainDataProvider();
#else
                data.DataProvider = new GameTerrainDataProvider(component, contentManager);
#endif

                data.DataProvider.LoadTerrainData(ref data.TerrainData);
                data.StreamingManager = new Streaming.StreamingManager(data.TerrainData, data.DataProvider);
                data.PhysicsManager = new Physics.PhysicsManager(data, entity.Scene, data.StreamingManager);
                data.GpuTextureManager = new GpuTextureManager(data, graphicsDevice, TerrainRuntimeData.RuntimeTextureSize, data.StreamingManager);

                var (stream, baseOffset) = data.DataProvider.OpenStreamingData();
                data.BaseOffset = baseOffset;
                data.TerrainStream = stream;

                data.TerrainDataUrl = component.TerrainData.Url;
                data.TerrainStreamDataUrl = component.TerrainStreamingData.Url;

                // Allocate streaming textures
                data.ShadowMap ??= Texture.New2D(graphicsDevice, TerrainRuntimeData.ShadowMapSize, TerrainRuntimeData.ShadowMapSize, PixelFormat.R10G10B10A2_UNorm, TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);

                data.IsInitialized = true;
                data.ChunksPerRowLod0 = data.TerrainData.Header.Size / data.TerrainData.Header.ChunkSize;
            }
        }
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var camera = Services.GetService<SceneSystem>().TryGetMainCamera();
        if (camera == null)
            return;

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>().GraphicsDevice;
        var debugTextSystem = Services.GetService<DebugTextSystem>();
        var graphicsContext = Services.GetService<GraphicsContext>();
        var contentManager = Services.GetService<ContentManager>();

        using var profilingScope = Profiler.Begin(ProfilingKeyChunk);

        foreach (var pair in ComponentDatas)
        {
            var component = pair.Key;
            var data = pair.Value;

            if (component.Material == null)
                continue;

            if (component.TerrainData == null || component.TerrainStreamingData == null)
                continue;

            if (!data.IsInitialized)
                continue;

            var modelRenderProcessor = EntityManager.GetProcessor<ModelRenderProcessor>();
            if (modelRenderProcessor == null)
                continue; // Just wait until it's available.

            // Get render model and setup mapping so terrain data can be retrieved in the render feature.
            if (data.RenderModel == null)
            {
                modelRenderProcessor!.RenderModels.TryGetValue(data.ModelComponent!, out var renderModel);

                if (renderModel == null) throw new Exception("render model not available");

                _modelToTerrainMap[renderModel] = data;
                data.RenderModel = renderModel;
            }

            // Sync material.
            if (data.ModelComponent!.Materials[0] != component.Material)
                data.ModelComponent.Materials[0] = component.Material;

            data.ModelComponent.Enabled = true;
            
            // Setup basic draw settings, it's global per terrain so makes sense to do it here and not in the render feature
            var terrainSize = data.TerrainData.Header.Size;
            var chunkSize = data.TerrainData.Header.ChunkSize;
            var lod0Distance = component.Lod0Distance;

            var chunksPerRowLod0 = terrainSize / chunkSize;
            var maxChunks = chunksPerRowLod0 * chunksPerRowLod0;

            data.Mesh.Draw.DrawCount = chunkSize * chunkSize * 6;

            // TODO: These sizes are very conservative ...
            if (data.ChunkData.Length != maxChunks)
            {
                data.ChunkData = new ChunkData[maxChunks];
                data.ChunkBuffer?.Dispose();
                data.ChunkBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, Marshal.SizeOf<ChunkData>(), true);

                data.ChunkInstanceData?.Dispose();
                data.ChunkInstanceData = Buffer.Structured.New(graphicsDevice, maxChunks, Marshal.SizeOf<int>(), true);
            }

            if (data.SectorToChunkMap.Length != maxChunks)
            {
                data.SectorToChunkMap = new int[maxChunks];
                data.SectorToChunkMapBuffer?.Dispose();
                data.SectorToChunkMapBuffer = Buffer.Structured.New(graphicsDevice, maxChunks, sizeof(int), true);
            }

            data.CameraPosition = camera.GetWorldPosition();

            data.PhysicsManager?.Update(data.CameraPosition.X, data.CameraPosition.Z);
            data.GpuTextureManager?.Update(graphicsContext);
            data.StreamingManager?.ProcessPendingCompletions(6);

            // Setup chunk lod, these are always based on the main camera position, frustum culling of the chunks are done in the render feature
            var maxLod = data.TerrainData.Header.MaxLod; // Max lod = single chunk
            var maxLodSetting = maxLod;
            if (data.MaximumLod >= 0)
                maxLodSetting = Math.Min(data.MaximumLod, maxLodSetting);

            var minLod = Math.Max(0, data.MinimumLod);

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

            data.ChunkCount = 0;

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

                    var chunkIndex = data.TerrainData.GetChunkIndex(lod, positionX, positionZ, chunksPerRowCurrentLod);

                    var chunkWorldPosition = new Vector3(positionX * chunkOffset + (chunkOffset * 0.5f), 0, positionZ * chunkOffset + (chunkOffset * 0.5f)) * data.UnitsPerTexel;

                    // Check lod distance
                    var lodDistance = lod0Distance * (1 << lod);

                    var extent = scale * data.UnitsPerTexel * chunkSize * 0.5f;
                    var maxHeight = data.TerrainData.Header.MaxHeight; // TODO: Should use max height for chunk but there is some issue with it ...
                    var heightRange = maxHeight;
                    var halfHeightRange = heightRange * 0.5f;

                    var bounds = new BoundingBoxExt
                    {
                        Center = chunkWorldPosition + new Vector3(0, halfHeightRange, 0),
                        Extent = new(extent, heightRange, extent)
                    };

                    var rect = new RectangleF(chunkWorldPosition.X - extent, chunkWorldPosition.Z - extent, extent * 2.0f, extent * 2.0f);
                    var cameraRect = new RectangleF(data.CameraPosition.X - lodDistance, data.CameraPosition.Z - lodDistance, lodDistance * 2.0f, lodDistance * 2.0f);

                    // Split if desired, otherwise add instance for current lod level
                    cameraRect.Intersects(ref rect, out var shouldSplit);
                    shouldSplit &= lod > minLod;
                    if (lod > maxLodSetting) shouldSplit = true;

                    // If max lod then skip if chunk is not resident yet
                    // Cannot happen for other lod's as the chunk wont be split if child chunks are not resident.
                    if (lod == maxLod && !data.GpuTextureManager!.RequestChunk(data.TerrainData.GetChunkIndex(lod, positionX, positionZ, chunksPerRowCurrentLod)))
                        continue;

                    // Request streaming if desired, chunk will only be split into next lod if all children are resident.
                    if (shouldSplit)
                    {
                        if (!data.GpuTextureManager!.RequestChunk(data.TerrainData.GetChunkIndex(lod - 1, positionZ * 2 * chunksPerRowNextLod + (positionX * 2)))) shouldSplit = false;
                        if (!data.GpuTextureManager!.RequestChunk(data.TerrainData.GetChunkIndex(lod - 1, positionZ * 2 * chunksPerRowNextLod + (positionX * 2 + 1)))) shouldSplit = false;
                        if (!data.GpuTextureManager!.RequestChunk(data.TerrainData.GetChunkIndex(lod - 1, (positionZ * 2 + 1) * chunksPerRowNextLod + (positionX * 2)))) shouldSplit = false;
                        if (!data.GpuTextureManager!.RequestChunk(data.TerrainData.GetChunkIndex(lod - 1, (positionZ * 2 + 1) * chunksPerRowNextLod + (positionX * 2 + 1)))) shouldSplit = false;
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
                                data.SectorToChunkMap[index] = data.ChunkCount;
                            }
                        }

                        var textureIndex = data.GpuTextureManager!.GetTextureIndex(chunkIndex);

                        var (tx, ty) = data.GpuTextureManager!.Heightmap!.GetCoordinates(textureIndex);

                        data.ChunkData[data.ChunkCount].UvX = tx;
                        data.ChunkData[data.ChunkCount].UvY = ty;

                        data.ChunkData[data.ChunkCount].LodLevel = (byte)lod;
                        data.ChunkData[data.ChunkCount].ChunkX = (byte)positionX;
                        data.ChunkData[data.ChunkCount].ChunkZ = (byte)positionZ;
                        data.ChunkData[data.ChunkCount].Scale = scale * data.UnitsPerTexel;
                        data.ChunkData[data.ChunkCount].Position = new(positionX * chunkOffset * data.UnitsPerTexel, 0, positionZ * chunkOffset * data.UnitsPerTexel);
                        data.ChunkData[data.ChunkCount].Bounds = bounds;
                        data.ChunkCount++;
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

            static byte GetLodDifference(ChunkData[] chunks, int[] sectorToChunkMap, int x, int z, int chunksPerRow, int ratioToLod0, int lod)
            {
                x = x * ratioToLod0;
                z = z * ratioToLod0;

                if (x < 0 || z < 0 || x >= chunksPerRow || z >= chunksPerRow)
                {
                    return 0;
                }
                else
                {
                    var chunkIndex = sectorToChunkMap[z * chunksPerRow + x];
                    if (chunkIndex == -1)
                        return 0;
                    return (byte)Math.Max(0, chunks[chunkIndex].LodLevel - lod);
                }
            }

            // Calculate lod differences between chunks
            for (var i = 0; i < data.ChunkCount; i++)
            {
                ref var chunk = ref data.ChunkData[i];
                scale = 1 << chunk.LodLevel;
                var chunksPerRow = terrainSize / (scale * chunkSize);

                var x = chunk.ChunkX;
                var z = chunk.ChunkZ;

                var ratioToLod0 = chunksPerRowLod0 / chunksPerRow;

                chunk.North = GetLodDifference(data.ChunkData, data.SectorToChunkMap, x, z - 1, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
                chunk.South = GetLodDifference(data.ChunkData, data.SectorToChunkMap, x, z + 1, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
                chunk.East = GetLodDifference(data.ChunkData, data.SectorToChunkMap, x + 1, z, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
                chunk.West = GetLodDifference(data.ChunkData, data.SectorToChunkMap, x - 1, z, chunksPerRowLod0, ratioToLod0, chunk.LodLevel);
            }

            //var maxLoadedChunks = (TerrainRuntimeData.RuntimeTextureSize / data.TerrainData.Header.ChunkTextureSize) * TerrainRuntimeData.RuntimeTextureSize / data.TerrainData.Header.ChunkTextureSize;
            //debugTextSystem.Print($"Terrain chunk count: {data.ChunkCount}", new(10, 240), new Color4(1, 0, 0, 1));
            //debugTextSystem.Print($"Resident chunks: {data.ResidentChunksCount}", new(10, 260), new Color4(1, 0, 0, 1));
            //debugTextSystem.Print($"Active chunks: {data.ActiveChunks.Count}", new(10, 280), new Color4(1, 0, 0, 1));
            //debugTextSystem.Print($"Pending chunks: {data.PendingChunks.Count}", new(10, 300), new Color4(1, 0, 0, 1));
            //debugTextSystem.Print($"Max loaded chunks: {maxLoadedChunks}", new(10, 320), new Color4(1, 0, 0, 1));
            //debugTextSystem.Print($"Physics chunks: {data.PhysicsEntities.Count}, Pool: {data.PhysicsEntityPool.Count}", new(10, 340), new Color4(1, 0, 0, 1));
            //debugTextSystem.Print($"Camera: {data.CameraPosition.X:0.0f} {data.CameraPosition.Y:0.0f} {data.CameraPosition.Z:0.0f}", new(10, 360), new Color4(1, 0, 0, 1));
        }
    }

    protected override void OnSystemAdd()
    {
        base.OnSystemAdd();

        VisibilityGroup.Tags.Set(TerrainRenderFeature.ModelToTerrainMap, _modelToTerrainMap);
    }

    protected override void OnSystemRemove()
    {
        base.OnSystemRemove();

        VisibilityGroup.Tags.Remove(TerrainRenderFeature.ModelToTerrainMap);
    }
}

