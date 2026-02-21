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
using StrideCommunity.ImGuiDebug;
using StrideTerrain.Rendering;
using StrideTerrain.TerrainSystem.Effects.Material;
using StrideTerrain.TerrainSystem.Rendering;
using StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static Hexa.NET.ImGui.ImGui;
using static StrideCommunity.ImGuiDebug.ImGuiExtension;

namespace StrideTerrain.TerrainSystem;

public class TerrainProcessor : EntityProcessor<TerrainComponent, TerrainRuntimeData>, IEntityComponentRenderProcessor
{
    private static readonly ProfilingKey ProfilingKeyUpdate = new("Terrain.Update");
    private static readonly ProfilingKey ProfilingKeyChunk = new("Terrain.Chunk");
    
    private readonly Dictionary<RenderModel, TerrainRuntimeData> _modelToTerrainMap = [];
    
    public VisibilityGroup VisibilityGroup { get; set; } = null!;
    public TerrainRuntimeData? TerrainData => _modelToTerrainMap.FirstOrDefault().Value;
    public Vector3? OverrideCameraPosition { get; set; }

    private DebugInterface? _debugInterface;

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

        _debugInterface ??= new(Services);

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
                _debugInterface.Data = null;
                continue;
            }

            if (component.TerrainData == null || component.TerrainStreamingData == null)
            {
                data.IsInitialized = false;
                _debugInterface.Data = null;
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
                data.VirtualTexturingSystem ??= new(Services, graphicsDevice);

                // Setup model.
                data.ModelComponent = entity.GetOrCreate<ModelComponent>();
                data.ModelComponent.Model ??= [data.MeshManager.Mesh];
                data.ModelComponent.Model.BoundingSphere = new(Vector3.Zero, 10000);
                data.ModelComponent.Model.BoundingBox = BoundingBox.FromSphere(data.ModelComponent.BoundingSphere);
                data.ModelComponent.IsShadowCaster = false;
                data.ModelComponent.Materials[0] = component.Material;
                data.ModelComponent.Enabled = false; // Stays disabled until everything is ready.
                data.ModelComponent.RenderGroup = RenderGroups.Terrain;

                data.TerrainDataUrl = component.TerrainData.Url;
                data.IsInitialized = true;

                _debugInterface.Data = data;
            }
        }
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var camera = Services.GetService<SceneSystem>()?.TryGetMainCamera();
        if (camera == null && OverrideCameraPosition == null)
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
            var cameraPosition = OverrideCameraPosition ?? camera!.GetWorldPosition();
            data.PhysicsManager?.Update(cameraPosition.X, cameraPosition.Z);
            data.GpuTextureManager?.Update(graphicsContext);
            data.StreamingManager?.ProcessPendingCompletions(1);
            data.MeshManager?.Update(cameraPosition, CollectionsMarshal.AsSpan(component.LodDistances));

            // Bind virtual texturing settings for terrain material.
            // TODO: Set in render feature and make it work for all materials that need to sample terrain.
            var parameters = data.ModelComponent.Materials[0].Passes[0].Parameters;
            parameters.Set(TerrainVirtualTextureKeys.VTMipBias, VTConstants.MipBias);
            parameters.Set(TerrainVirtualTextureKeys.VTMaxAniso, 4.0f);
            float terrainSize = data.TerrainData.Header.Size * data.UnitsPerTexel;
            parameters.Set(TerrainVirtualTextureKeys.VTResolution, (terrainSize / VTConstants.BaseTileWorld) * VTConstants.TileSize);
            parameters.Set(TerrainVirtualTextureKeys.VTCameraPosition, cameraPosition);

            // Update virtual texturing
            if (data.VirtualTexturingSystem != null)
            {
                data.VirtualTexturingSystem.TileRenderer.MaterialDiffuseRoughnessArray = parameters.Get(TerrainMaterialSamplingKeys.DiffuseRoughnessArray);
                data.VirtualTexturingSystem.TileRenderer.MaterialNormalArray = parameters.Get(TerrainMaterialSamplingKeys.NormalArray);
                data.VirtualTexturingSystem.Update(context.GetThreadContext(), cameraPosition, data);

                var vts = data.VirtualTexturingSystem;
                parameters.Set(TerrainVirtualTextureKeys.ClipmapOriginsPacked0, vts.ClipmapOriginsPacked0);
                parameters.Set(TerrainVirtualTextureKeys.ClipmapOriginsPacked1, vts.ClipmapOriginsPacked1);
                parameters.Set(TerrainVirtualTextureKeys.ClipmapOriginsPacked2, vts.ClipmapOriginsPacked2);
                parameters.Set(TerrainVirtualTextureKeys.ClipmapOriginsPacked3, vts.ClipmapOriginsPacked3);
                parameters.Set(TerrainVirtualTextureKeys.ClipmapOriginsPacked4, vts.ClipmapOriginsPacked4);
                parameters.Set(TerrainVirtualTextureSamplingKeys.PhysicalDiffuse, data.VirtualTexturingSystem.PhysicalAtlas.DiffuseAtlas);
                parameters.Set(TerrainVirtualTextureSamplingKeys.PhysicalRoughness, data.VirtualTexturingSystem.PhysicalAtlas.RoughnessAtlas);
                parameters.Set(TerrainVirtualTextureSamplingKeys.PhysicalNormal, data.VirtualTexturingSystem.PhysicalAtlas.NormalAtlas);
            }
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

    class DebugInterface(IServiceRegistry services) : BaseWindow(services)
    {
        public TerrainRuntimeData? Data;

        protected override void OnDestroy()
        {
        }

        protected override void OnDraw(bool collapsed)
        {
            if (Data == null || Data.GpuTextureManager== null || Data.VirtualTexturingSystem == null) return;

            if (CollapsingHeader("Heightmap"))
            {
                Image(Data.GpuTextureManager.Heightmap.AtlasTexture, 512, 512);
            }

            if (CollapsingHeader("VT Diffuse"))
            {
                Image(Data.VirtualTexturingSystem.PhysicalAtlas.DiffuseAtlas, 512, 512);
            }

            if (CollapsingHeader("VT Normal"))
            {
                Image(Data.VirtualTexturingSystem.PhysicalAtlas.NormalAtlas, 512, 512);
            }

            if (CollapsingHeader("VT Roughness"))
            {
                Image(Data.VirtualTexturingSystem.PhysicalAtlas.RoughnessAtlas, 512, 512);
            }
        }
    }
}

