using Stride.Core;
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
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StrideTerrain.TerrainSystem;

public class TerrainProcessor : EntityProcessor<TerrainComponent, TerrainRuntimeData>, IEntityComponentRenderProcessor
{
    private static readonly ProfilingKey ProfilingKeyUpdate = new("Terrain.Update");
    private static readonly ProfilingKey ProfilingKeyChunk = new("Terrain.Chunk");

    private readonly Dictionary<RenderModel, TerrainRuntimeData> _modelToTerrainMap = [];
    private SpriteBatch? _spriteBatch;

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

        var graphicsDevice = Services.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;
        var graphicsContext = Services.GetSafeServiceAs<GraphicsContext>();
        var contentManager = Services.GetSafeServiceAs<ContentManager>();

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
            data.MaximumLod = component.MaximumLod;
            data.MinimumLod = component.MinimumLod;
            data.ShadowBlurRadius = component.ShadowBlurRadius;
            data.ShadowBlurSigmaRatio = component.ShadowBlurSigmaRatio;

            // Load initial data.
            if (data.TerrainDataUrl != component.TerrainData.Url)
            {
                if (data.RenderModel != null)
                    _modelToTerrainMap.Remove(data.RenderModel);

                // Clean up old data if needed
                data.Dispose();

                var entity = component.Entity;

#if GAME_EDITOR
                data.DataProvider = new EditorTerrainDataProvider();
#else
                data.DataProvider = new GameTerrainDataProvider(component, contentManager);
#endif

                // Load terrain data and setup the various managers.
                data.DataProvider.LoadTerrainData(ref data.TerrainData);
                data.ChunksPerRowLod0 = data.TerrainData.Header.Size / data.TerrainData.Header.ChunkSize;

                data.StreamingManager = new Streaming.StreamingManager(data.TerrainData, data.DataProvider);
#if !GAME_EDITOR
                data.PhysicsManager = new Physics.PhysicsManager(data, entity.Scene, data.StreamingManager);
#endif
                data.GpuTextureManager = new GpuTextureManager(data.TerrainData, graphicsDevice, TerrainRuntimeData.RuntimeTextureSize, data.StreamingManager);
                data.MeshManager = new MeshManager(data, graphicsDevice, data.GpuTextureManager);

                // Setup model.
                data.ModelComponent = entity.GetOrCreate<ModelComponent>();
                data.ModelComponent.Model ??= [data.MeshManager.Mesh];
                data.ModelComponent.Model.BoundingSphere = new(Vector3.Zero, 10000);
                data.ModelComponent.Model.BoundingBox = BoundingBox.FromSphere(data.ModelComponent.BoundingSphere);
                data.ModelComponent.IsShadowCaster = false;
                data.ModelComponent.Materials[0] = component.Material;
                data.ModelComponent.Enabled = false; // Stays disabled until everything is ready.
                data.ModelComponent.RenderGroup = component.RenderGroup;

                data.TerrainDataUrl = component.TerrainData.Url;
                data.IsInitialized = true;
            }
        }
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var camera = Services.GetService<SceneSystem>()?.TryGetMainCamera();
        if (camera == null)
            return;

        var modelRenderProcessor = EntityManager.GetProcessor<ModelRenderProcessor>();
        if (modelRenderProcessor == null)
            return; // Just wait until it's available.

        var graphicsDevice = Services.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;
        var debugTextSystem = Services.GetSafeServiceAs<DebugTextSystem>();
        var graphicsContext = Services.GetSafeServiceAs<GraphicsContext>();
        var contentManager = Services.GetSafeServiceAs<ContentManager>();

        using var profilingScope = Profiler.Begin(ProfilingKeyChunk);

        foreach (var pair in ComponentDatas)
        {
            var component = pair.Key;
            var data = pair.Value;

            if (component.Material == null || !data.IsInitialized)
                continue;

            // Get render model and setup mapping so terrain data can be retrieved in the render feature.
            if (data.RenderModel == null)
            {
                modelRenderProcessor!.RenderModels.TryGetValue(data.ModelComponent!, out var renderModel);

                if (renderModel == null) throw new Exception("render model not available");

                _modelToTerrainMap[renderModel] = data;
                data.RenderModel = renderModel;
            }

            // Sync material if changed.
            if (data.ModelComponent!.Materials[0] != component.Material)
                data.ModelComponent.Materials[0] = component.Material;

            // Model can now be enabled.
            data.ModelComponent.Enabled = true;

            // Update all managers.
            var cameraPosition = camera.GetWorldPosition();
            data.PhysicsManager?.Update(cameraPosition.X, cameraPosition.Z);
            data.GpuTextureManager?.Update(graphicsContext);
            data.StreamingManager?.ProcessPendingCompletions(1);
            data.MeshManager?.Update(cameraPosition, CollectionsMarshal.AsSpan(component.LodDistances));

            //var maxLoadedChunks = (TerrainRuntimeData.RuntimeTextureSize / data.TerrainData.Header.ChunkTextureSize) * TerrainRuntimeData.RuntimeTextureSize / data.TerrainData.Header.ChunkTextureSize;
            if (data.StreamingManager != null && data.GpuTextureManager != null)
            {
                //_spriteBatch ??= new(graphicsDevice);

                //_spriteBatch.Begin(graphicsContext);
                //_spriteBatch.Draw(data.GpuTextureManager.Heightmap.AtlasTexture, new RectangleF(512, 512, 512, 512), Color4.White);
                //_spriteBatch.End();

                //debugTextSystem.Print($"Pending streaming requests: {data.StreamingManager.PendingStreamingRequests}", new(10, 240), new Color4(1, 0, 0, 1));
                //debugTextSystem.Print($"Pending streaming completions: {data.StreamingManager.PendingCompletions}", new(10, 260), new Color4(1, 0, 0, 1));
                //debugTextSystem.Print($"Texture Atlas Free Slots: {data.GpuTextureManager.FreeSlots}", new(10, 280), new Color4(1, 0, 0, 1));
            }
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

