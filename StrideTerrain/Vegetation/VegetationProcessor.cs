using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Collections;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Rendering;
using StrideTerrain.Rendering.Profiling;
using StrideTerrain.Vegetation.Effects;
using StrideTerrain.Vegetation.Impostors;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Buffer = Stride.Graphics.Buffer;
using Half = System.Half;

namespace StrideTerrain.Vegetation;

public class VegetationProcessor : EntityProcessor<VegetationComponent, VegetationProcessor.RuntimeData>
{
    private static readonly ProfilingKey ProfilingKeyImpostorsDraw = new("Trees.Draw.Impostors");
    private static readonly ProfilingKey ProfilingKeyInstancedDraw = new("Trees.Draw.Instanced");
    private static readonly ProfilingKey ProfilingKeyBake = new("Trees.Bake.Impostors");
    private const int GridSize = 128;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true
    };

    private ImpostorBaker? _baker;

    protected override RuntimeData GenerateComponentData(Entity entity, VegetationComponent component)
        => new();

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] VegetationComponent component, [NotNull] RuntimeData data)
    {
        base.OnEntityComponentRemoved(entity, component, data);
        data.Dispose();
    }

    public override void Update(GameTime time)
    {
        base.Update(time);

        var graphicsDevice = Services.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;
        var sceneSystem = Services.GetSafeServiceAs<SceneSystem>();

        var camera = sceneSystem.TryGetMainCamera();
        if (camera == null)
            return;

        var cameraPosition = camera.GetWorldPosition();

        foreach (var componentData in ComponentDatas)
        {
            var component = componentData.Key;
            var data = componentData.Value;

            if (IsDirty(component, data))
            {
                data.Dispose();
                if (!InitializeRuntimeData(component, data))
                    continue;
            }

            // Nothing can be drawn until the atlas exists - the billboard quad is sized from the
            // baked bounds, so drawing before the bake would use garbage.
            if (!data.IsBaked)
                continue;

            UpdateMeshInstances(component, data, cameraPosition);

            data.ImpostorEntity!.Get<ModelComponent>().Enabled = Enabled;
        }

        static bool IsDirty(VegetationComponent component, RuntimeData data)
            => data.LodEntities.Count == 0
            || data.ImpostorMaterial != component.ImpostorMaterial
            || data.Model != component.Model
            || data.GridSizeUsed != component.ImpostorGridSize
            || data.FrameResolutionUsed != component.ImpostorFrameResolution;
    }

    /// <summary>
    /// Baking needs a command list, which only exists on the render thread, so it happens here
    /// rather than in Update. Each model is baked exactly once.
    /// </summary>
    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = Services.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;

        foreach (var componentData in ComponentDatas)
        {
            var component = componentData.Key;
            var data = componentData.Value;

            if (data.IsBaked || data.Model == null || data.ImpostorMaterial == null || data.LodModels.Count == 0)
                continue;

            if (_baker == null)
            {
                _baker = new ImpostorBaker(Services, graphicsDevice);

                // Set to a folder to have every baked atlas written out as a PNG. Off by default.
                ImpostorBaker.DumpPath = System.Environment.GetEnvironmentVariable("STRIDETERRAIN_IMPOSTOR_DUMP");
            }

            var drawContext = context.GetThreadContext();

            using (drawContext.QueryManager.BeginProfile(Color4.Black, ProfilingKeyBake))
            {
                // LOD0, never data.Model. Since the vegetation models point at the LOD chain, the
                // raw model holds every level's geometry stacked on top of itself plus the impostor
                // card UE bakes in - rendering that gives a silhouette several layers of alpha
                // tested foliage deep, which comes out far darker than the tree actually is.
                data.Atlas = _baker.Bake(drawContext, data.LodModels[0], component.ImpostorGridSize, component.ImpostorFrameResolution);
            }

            if (data.Atlas == null)
            {
                // Mark as baked anyway so we do not retry a model that can never succeed every
                // single frame.
                data.IsBaked = true;
                continue;
            }

            BuildPositionsBuffer(graphicsDevice, data);
            BindImpostorMaterial(component, data);

            data.IsBaked = true;
        }
    }

    /// <summary>
    /// Buckets every instance inside the impostor handover distance into its LOD level and uploads
    /// one instance array per level. Instances inside the fade band are still submitted so mesh and
    /// impostor overlap there and can dither between each other.
    /// </summary>
    private static void UpdateMeshInstances(VegetationComponent component, RuntimeData data, Vector3 cameraPosition)
    {
        for (var i = 0; i < data.LodMatrices.Count; i++)
            data.LodMatrices[i].Clear();

        var meshDistance = component.ImpostorLodDistance;
        var meshDistanceSquared = meshDistance * meshDistance;

        var start = GetGridPosition(cameraPosition.X - meshDistance, cameraPosition.Z - meshDistance);
        var end = GetGridPosition(cameraPosition.X + meshDistance, cameraPosition.Z + meshDistance);

        var lastLod = data.LodMatrices.Count - 1;

        for (var z = start.Z; z <= end.Z; z++)
        {
            for (var x = start.X; x <= end.X; x++)
            {
                if (!data.GridPositions.TryGetValue((x, z), out var positions))
                    continue;

                for (var i = 0; i < positions.Count; i++)
                {
                    var distanceSquared = (cameraPosition.XZ() - new Vector2(positions[i].X, positions[i].Z)).LengthSquared();
                    if (distanceSquared >= meshDistanceSquared)
                        continue;

                    // Compared squared to keep the per instance cost to a multiply; the thresholds
                    // are squared once per frame in RefreshLodThresholds.
                    var lod = lastLod;
                    for (var level = 0; level < data.LodThresholdsSquared.Count; level++)
                    {
                        if (distanceSquared < data.LodThresholdsSquared[level])
                        {
                            lod = level;
                            break;
                        }
                    }

                    data.LodMatrices[lod].Add(Matrix.Scaling(positions[i].W) * Matrix.Translation(positions[i].XYZ()));
                }
            }
        }

        for (var lod = 0; lod < data.LodEntities.Count; lod++)
        {
            var matrices = data.LodMatrices[lod];
            var model = data.LodEntities[lod].Get<ModelComponent>();
            var instancing = data.LodEntities[lod].Get<InstancingComponent>();

            if (matrices.Count > 0)
            {
                instancing.Enabled = true;
                ((InstancingUserArray)instancing.Type).UpdateWorldMatrices(matrices.Items, matrices.Count);
            }
            else
            {
                instancing.Enabled = false;
            }

            model.Enabled = matrices.Count > 0;
        }
    }

    private static void BuildPositionsBuffer(GraphicsDevice graphicsDevice, RuntimeData data)
    {
        var atlas = data.Atlas!;
        var packed = new Vector4[data.Instances.Count];

        for (var i = 0; i < data.Instances.Count; i++)
        {
            var instance = data.Instances[i];
            var scale = instance.W;

            // Quad size comes from the baked bounds rather than a hand tuned ImpostorSize, so the
            // billboard silhouette always matches the mesh exactly.
            packed[i] = new Vector4(instance.X, instance.Y, instance.Z,
                PackScale(atlas.WorldSize.X * scale, atlas.WorldSize.Y * scale));
        }

        data.PositionsBuffer = Buffer.New(graphicsDevice, (ReadOnlySpan<Vector4>)packed,
            BufferFlags.StructuredBuffer | BufferFlags.ShaderResource | BufferFlags.UnorderedAccess);
    }

    private static void BindImpostorMaterial(VegetationComponent component, RuntimeData data)
    {
        var material = data.ImpostorMaterial!;
        if (material.Passes == null || material.Passes.Count == 0)
            return;

        var atlas = data.Atlas!;
        var parameters = material.Passes[0].Parameters;

        parameters.Set(MaterialImpostorDisplacementFeatureKeys.Positions, data.PositionsBuffer);
        parameters.Set(MaterialImpostorDisplacementFeatureKeys.ImpostorWorldSize, atlas.WorldSize);
        parameters.Set(MaterialImpostorDisplacementFeatureKeys.ImpostorCenterOffset, atlas.CenterOffset);
        parameters.Set(MaterialImpostorDisplacementFeatureKeys.ImpostorGridSize, atlas.GridSize);

        var fadeEnd = MathF.Max(component.ImpostorLodDistance - component.ImpostorFadeRange, 0.0f);
        parameters.Set(MaterialImpostorDisplacementFeatureKeys.ImpostorFadeStart, component.ImpostorLodDistance);
        parameters.Set(MaterialImpostorDisplacementFeatureKeys.ImpostorFadeEnd, fadeEnd);

        parameters.Set(ComputeColorImpostorDiffuseKeys.ImpostorDiffuseAtlas, atlas.Diffuse);
        parameters.Set(ComputeColorImpostorNormalKeys.ImpostorNormalAtlas, atlas.Normal);
    }

    private static bool InitializeRuntimeData(VegetationComponent component, RuntimeData data)
    {
        data.ImpostorMaterial = component.ImpostorMaterial;
        data.Model = component.Model;
        data.GridSizeUsed = component.ImpostorGridSize;
        data.FrameResolutionUsed = component.ImpostorFrameResolution;

        if (data.Model == null || data.ImpostorMaterial == null || string.IsNullOrEmpty(component.InstancesJson))
            return false;

        var instances = JsonSerializer.Deserialize<List<Vector4>>(component.InstancesJson, _jsonOptions)!;
        if (instances.Count == 0)
            return false;

        data.Instances = instances;

        for (var i = 0; i < instances.Count; i++)
        {
            var gridPosition = GetGridPosition(instances[i].X, instances[i].Z);
            if (!data.GridPositions.TryGetValue(gridPosition, out var cell))
            {
                cell = [];
                data.GridPositions.Add(gridPosition, cell);
            }

            cell.Add(instances[i]);
        }

        var entity = component.Entity;

        data.ImpostorEntity =
        [
            new ProfilingKeyComponent
            {
                ProfilingKey  = ProfilingKeyImpostorsDraw
            }
        ];

        var impostorModel = data.ImpostorEntity.GetOrCreate<ModelComponent>();
        impostorModel.Model ??= [new Mesh()
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                VertexBuffers = [],
                DrawCount = instances.Count * 6,
            },
            BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
        }];
        impostorModel.RenderGroup = RenderGroups.Impostors;
        impostorModel.Model.BoundingSphere = new(Vector3.Zero, 10000);
        impostorModel.Model.BoundingBox = BoundingBox.FromSphere(impostorModel.BoundingSphere);
        impostorModel.IsShadowCaster = false;
        impostorModel.Materials[0] = data.ImpostorMaterial;
        impostorModel.Enabled = false;

        entity.AddChild(data.ImpostorEntity);

        // One entity per LOD level. Stride's instancing binds a single model, so distinct levels
        // need distinct draws rather than a mesh swap on one component.
        data.LodModels = VegetationLods.Split(data.Model);

        VegetationLods.GetLodDistances(component.LodDistances, data.LodModels.Count,
            component.ImpostorLodDistance, data.LodThresholds);

        data.LodThresholdsSquared.Clear();
        foreach (var distance in data.LodThresholds)
            data.LodThresholdsSquared.Add(distance * distance);

        for (var lod = 0; lod < data.LodModels.Count; lod++)
        {
            var lodEntity = new Entity($"VegetationLod{lod}")
            {
                new ProfilingKeyComponent
                {
                    ProfilingKey  = ProfilingKeyInstancedDraw
                },
                new ModelComponent
                {
                    BoundingSphere = new(Vector3.Zero, 10000),
                    BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
                    Model = data.LodModels[lod],
                    Enabled = false,
                    // Only the closest level casts shadows. The reduced levels sit far enough out
                    // that their shadows are subpixel, and casting from all of them would multiply
                    // the shadow pass cost for no visible gain.
                    IsShadowCaster = lod == 0
                },
                new InstancingComponent
                {
                    Enabled = false,
                    Type = new InstancingUserArray
                    {
                        WorldMatrices = []
                    }
                }
            };

            data.LodEntities.Add(lodEntity);
            data.LodMatrices.Add(new FastList<Matrix>());

            component.Entity.AddChild(lodEntity);
        }

        return true;
    }

    static (int X, int Z) GetGridPosition(float x, float z)
            => ((int)(x / GridSize), (int)(z / GridSize));

    static float PackScale(float scaleX, float scaleY)
    {
        var hx = (Half)scaleX;
        var hy = (Half)scaleY;

        uint packed = ((uint)BitConverter.HalfToUInt16Bits(hy) << 16) | BitConverter.HalfToUInt16Bits(hx);

        return BitConverter.Int32BitsToSingle((int)packed);
    }

    protected override void OnSystemRemove()
    {
        base.OnSystemRemove();

        _baker?.Dispose();
        _baker = null;
    }

    public class RuntimeData : IDisposable
    {
        public Buffer? PositionsBuffer;
        public Entity? ImpostorEntity;

        // One entry per LOD level, highest detail first.
        public List<Entity> LodEntities = [];
        public List<Model> LodModels = [];
        public List<FastList<Matrix>> LodMatrices = [];
        public List<float> LodThresholds = [];
        public List<float> LodThresholdsSquared = [];
        public Dictionary<(int, int), List<Vector4>> GridPositions = [];

        public Material? ImpostorMaterial;
        public Model? Model;

        public List<Vector4> Instances = [];

        public ImpostorAtlas? Atlas;
        public bool IsBaked;
        public int GridSizeUsed;
        public int FrameResolutionUsed;

        public void Dispose()
        {
            PositionsBuffer?.Dispose();
            PositionsBuffer = null;

            Atlas?.Dispose();
            Atlas = null;
            IsBaked = false;

            foreach (var lodEntity in LodEntities)
            {
                lodEntity.SetParent(null);
                lodEntity.Scene = null;
            }

            LodEntities.Clear();
            LodModels.Clear();
            LodMatrices.Clear();
            LodThresholds.Clear();
            LodThresholdsSquared.Clear();

            if (ImpostorEntity != null)
            {
                ImpostorEntity.SetParent(null);
                ImpostorEntity.Scene = null;
                ImpostorEntity = null;
            }

            ImpostorMaterial = null;
            Model = null;
            Instances.Clear();
            GridPositions.Clear();
        }
    }
}
