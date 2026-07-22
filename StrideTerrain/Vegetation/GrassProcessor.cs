using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using StrideTerrain.Rendering;
using StrideTerrain.Rendering.Profiling;
using StrideTerrain.TerrainSystem;
using StrideTerrain.TerrainSystem.Effects;
using System;
using System.Collections.Generic;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.Vegetation;

public class GrassProcessor : EntityProcessor<GrassComponent, GrassProcessor.RuntimeData>, IEntityComponentRenderProcessor
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Grass.Draw");

    private readonly Dictionary<RenderModel, RenderGrass> _modelGrassMap = [];
    private ModelRenderProcessor _modelRenderProcessor = null!;
    private ComputeEffectShader? _grassPopulateInstancesShader;

    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    public GrassProcessor()
        : base(typeof(ModelComponent))
    {
        Order = 100;
    }

    protected override void OnSystemAdd()
    {
        base.OnSystemAdd();

        VisibilityGroup.Tags.Set(GrassRenderFeature.ModelToGrassMap, _modelGrassMap);

        _modelRenderProcessor = EntityManager.GetProcessor<ModelRenderProcessor>();
        if (_modelRenderProcessor == null)
        {
            _modelRenderProcessor = new ModelRenderProcessor();
            EntityManager.Processors.Add(_modelRenderProcessor);
        }
    }

    protected override void OnSystemRemove()
    {
        VisibilityGroup.Tags.Remove(GrassRenderFeature.ModelToGrassMap);
        base.OnSystemRemove();
    }

    protected override RuntimeData GenerateComponentData([NotNull] Entity entity, [NotNull] GrassComponent component)
        => new();

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] GrassComponent component, [NotNull] RuntimeData data)
    {
        base.OnEntityComponentRemoved(entity, component, data);

        if (data.RenderModel != null)
        {
            _modelGrassMap.Remove(data.RenderModel);
        }

        data.RenderGrass?.IndirectBuffer?.Dispose();
        data.RenderGrass?.InstancesCounterBuffer?.Dispose();
        data.RenderGrass?.CulledWorldBuffer?.Dispose();
        data.RenderGrass?.CulledWorldInverseBuffer?.Dispose();
        data.RenderGrass?.CulledCounterBuffer?.Dispose();
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = context.RenderSystem.GraphicsDevice;
        var sceneSystem = Services.GetSafeServiceAs<SceneSystem>();

        var renderDrawContext = context.GetThreadContext();

        _grassPopulateInstancesShader ??= new(context)
        {
            ShaderSourceName = "GrassPopulateInstances"
        };

        var camera = sceneSystem.TryGetMainCamera();
        if (camera == null)
            return;

        var terrainProcessor = sceneSystem.SceneInstance.Processors.Get<TerrainProcessor>();
        if (terrainProcessor == null)
            return;

        var terrain = terrainProcessor.TerrainData;
        if (terrain?.MeshManager?.IsReady != true)
            return;

        var cameraPosition = camera.GetWorldPosition();

        var (_, lodLevalAtCamera) = terrain.GetAtlasUv(cameraPosition.X, cameraPosition.Z);
        if (lodLevalAtCamera != 0)
            return;

        Span<uint> emptyBuffer = [0];

        foreach (var componentData in ComponentDatas)
        {
            var component = componentData.Key;
            var data = componentData.Value;

            if (data.RenderGrass.IndirectBuffer == null || data.RenderGrass.InstancesBuffer == null
                || data.Size != component.Size || data.RenderModel == null || component.Model != data.Model)
            {
                if (data.RenderModel != null)
                {
                    _modelGrassMap.Remove(data.RenderModel);
                }

                var oldRenderModel = data.RenderModel;
                data.Model = component.Model;
                if (data.Model == null 
                    || !_modelRenderProcessor.RenderModels.TryGetValue(data.Model, out data.RenderModel)
                    || data.RenderModel?.Meshes == null
                    || data.RenderModel.Meshes.Length != 1
                    || data.RenderModel.Meshes[0]?.ActiveMeshDraw == null)
                {
                    data.RenderModel = null;
                    continue;
                }

                _modelGrassMap[data.RenderModel] = data.RenderGrass;

                if (data.RenderModel != oldRenderModel)
                {
                    var boundingRadius = data.RenderModel!.Model.BoundingSphere.Radius;
                    if (boundingRadius <= 0)
                        boundingRadius = 1; // No idea why it keeps being 0, apparently bug in stride!

                    data.RenderGrass.BoundingRadius = boundingRadius;

                    // A bit ugly
                    data.Model.Model.Meshes[0].BoundingSphere = new(Vector3.Zero, 1000000);
                    data.Model.Model.Meshes[0].BoundingBox = new(new(-10000, -10000, -10000), new(10000, 10000, 10000));
                }

                data.Model.RenderGroup = RenderGroups.Grass;
                
                data.Size = component.Size;

                data.RenderGrass.IndirectBuffer?.Dispose();
                data.RenderGrass.InstancesBuffer?.Dispose();
                data.RenderGrass.InstancesCounterBuffer?.Dispose();
                data.RenderGrass.CulledWorldBuffer?.Dispose();
                data.RenderGrass.CulledWorldInverseBuffer?.Dispose();
                data.RenderGrass.CulledCounterBuffer?.Dispose();

                DrawArgs[] drawArgs = [new()
                {
                    BaseVertexLocation = 0,
                    StartIndexLocation = (uint)data.RenderModel!.Meshes[0].ActiveMeshDraw.StartLocation,
                    IndexCountPerInstance = (uint)data.RenderModel!.Meshes[0].ActiveMeshDraw.DrawCount,
                    InstanceCount = 0,
                    StartInstanceLocation = 0
                }];

                data.RenderGrass.IndirectBuffer = Buffer.New(graphicsDevice, (ReadOnlySpan<DrawArgs>)drawArgs, BufferFlags.ArgumentBuffer);

                var bufferSize = data.Size * data.Size;
                data.RenderGrass.InstancesBuffer = Buffer.Structured.New(graphicsDevice, bufferSize, sizeof(float) * 8, true);
                data.RenderGrass.InstancesCounterBuffer = Buffer.Raw.New(graphicsDevice, sizeof(uint), BufferFlags.UnorderedAccess | BufferFlags.ShaderResource);
                data.RenderGrass.CulledWorldBuffer = Buffer.Structured.New<Matrix>(graphicsDevice, bufferSize, true);
                data.RenderGrass.CulledWorldInverseBuffer = Buffer.Structured.New<Matrix>(graphicsDevice, bufferSize, true);
                data.RenderGrass.CulledCounterBuffer = Buffer.Raw.New(graphicsDevice, sizeof(uint), BufferFlags.UnorderedAccess | BufferFlags.ShaderResource);

                data.RenderGrass.InstancesBuffer.Name = "GrassInstances";
                data.RenderGrass.InstancesCounterBuffer.Name = "GrassInstancesCounter";

                component.Entity.GetOrCreate<ProfilingKeyComponent>().ProfilingKey = ProfilingKeyDraw;
            }

            // Clear instance counter
            data.RenderGrass.InstancesCounterBuffer!.SetData(renderDrawContext.CommandList, emptyBuffer);
            data.RenderGrass.Size = data.Size;

            // Populate instances
            var rng = new RandomSeed();
            var rngSeed = (uint)component.Seed;
            var seed = new Vector2(rng.GetFloat(rngSeed), rng.GetFloat(rngSeed + 1));

            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.CameraPosition, cameraPosition);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.Instances, data.RenderGrass.InstancesBuffer);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.InstancesCounter, data.RenderGrass.InstancesCounterBuffer);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.Size, (uint)data.Size);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.CellSize, component.CellSize);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.FadeStartFraction, component.FadeStartFraction);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.MinScale, component.MinScale);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.MaxScale, component.MaxScale);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.Clumpiness, component.Clumpiness);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.ClumpSize, component.ClumpSize);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.Seed, seed);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.RotateAlongTerrainNormal, component.RotateAlongTerrainNormal);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.ValidBackgroundTexturesIds, component.ValidBackgroundTexturesIds);
            _grassPopulateInstancesShader.Parameters.Set(GrassPopulateInstancesKeys.InstanceCount, (uint)(data.Size * data.Size));

            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.Heightmap, terrain.GpuTextureManager!.Heightmap.AtlasTexture);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.TerrainNormalMap, terrain.GpuTextureManager!.NormalMap.AtlasTexture);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.TerrainControlMap, terrain.GpuTextureManager!.ControlMap.AtlasTexture);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.SectorToChunkMapBuffer, terrain.MeshManager!.SectorToChunkMapBuffer);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.ChunkBuffer, terrain.MeshManager!.ChunkBuffer);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.ChunkSize, (uint)terrain.TerrainData.Header.ChunkSize);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.ChunksPerRow, (uint)terrain.ChunksPerRowLod0);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.MaxHeight, terrain.TerrainData.Header.MaxHeight);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.InvTerrainTextureSize, TerrainRuntimeData.InvRuntimeTextureSize);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.TerrainTextureSize, TerrainRuntimeData.RuntimeTextureSize);
            _grassPopulateInstancesShader.Parameters.Set(TerrainDataKeys.InvUnitsPerTexel, 1.0f / terrain.TerrainData.Header.UnitsPerTexel);

            _grassPopulateInstancesShader.ThreadGroupCounts = new(ComputeHelpers.DispatchSize(8, data.Size), ComputeHelpers.DispatchSize(8, data.Size), 1);
            _grassPopulateInstancesShader.ThreadNumbers = new(8, 8, 1);

            _grassPopulateInstancesShader.Draw(renderDrawContext, "Grass.PopulateInstances");
        }
    }

    public class RuntimeData
    {
        public ModelComponent? Model;
        public RenderGrass RenderGrass = new();
        public RenderModel? RenderModel;
        public int Size = 128;
    }
}

public class RenderGrass
{
    public Buffer? IndirectBuffer;
    public Buffer? InstancesBuffer;
    public Buffer? InstancesCounterBuffer;
    public Buffer? CulledWorldBuffer;
    public Buffer? CulledWorldInverseBuffer;
    public Buffer? CulledCounterBuffer;
    public int Size;
    public float BoundingRadius;
}