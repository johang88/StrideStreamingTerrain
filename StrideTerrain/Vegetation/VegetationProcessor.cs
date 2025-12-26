using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Engine;
using Stride.Graphics;
using System.Collections.Generic;
using System.Text.Json;
using System;
using Buffer = Stride.Graphics.Buffer;
using Stride.Core.Diagnostics;
using Stride.Rendering;
using StrideTerrain.Vegetation.Effects;
using Half = System.Half;
using System.Linq;
using Stride.Core;
using StrideTerrain.Rendering.Profiling;
using Stride.Core.Collections;
using StrideTerrain.Rendering;
using Stride.Games;
using Stride.Core.Annotations;
using System.IO;
using System.Runtime.InteropServices;

namespace StrideTerrain.Vegetation;

public class VegetationProcessor : EntityProcessor<VegetationComponent, VegetationProcessor.RuntimeData>
{
    private static readonly ProfilingKey ProfilingKeyImpostorsDraw = new("Trees.Draw.Impostors");
    private static readonly ProfilingKey ProfilingKeyInstancedDraw = new("Trees.Draw.Instanced");
    private const int GridSize = 128;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true
    };

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
                if (!InitializeRuntimeData(graphicsDevice, component, data))
                    continue;
            }

            if (data.ImpostorMaterial?.Passes == null || data.ImpostorMaterial.Passes.Count == 0)
                continue;

            data.ImpostorMaterial!.Passes[0].Parameters.Set(MaterialImpostorDisplacementFeatureKeys.Positions, data.PositionsBuffer);
            data.ImpostorMaterial.Passes[0].Parameters.Set(MaterialImpostorDisplacementFeatureKeys.LodDistance, component.ImpostorLodDistance);

            // TODO: This should be improved and have multi lod support
            data.InstancingWorldMatrices.Clear();
            var lodDistance = component.ImpostorLodDistance;
            var lodDistanceSquared = lodDistance * lodDistance;
            var start = GetGridPosition(cameraPosition.X - lodDistance, cameraPosition.Z - lodDistance);
            var end = GetGridPosition(cameraPosition.X + lodDistance, cameraPosition.Z + lodDistance);
            for (var z = start.Z; z <= end.Z; z++)
            {
                for (var x = start.X; x <= end.X; x++)
                {
                    if (!data.GridPositions.TryGetValue((x, z), out var positions))
                        continue;

                    for (var i = 0; i < positions.Count; i++)
                    {
                        var distanceSquared = (cameraPosition.XZ() - new Vector2(positions[i].X, positions[i].Z)).LengthSquared();
                        if (distanceSquared < lodDistanceSquared)
                        {
                            data.InstancingWorldMatrices.Add(Matrix.Scaling(positions[i].W) * Matrix.Translation(positions[i].XYZ()));
                        }
                    }
                }
            }

            var model = data.InstancingEntity!.Get<ModelComponent>();
            var instancing = data.InstancingEntity.Get<InstancingComponent>();

            if (data.InstancingWorldMatrices.Count > 0)
            {
                model.Enabled = true;
                instancing.Enabled = true;

                var instancingUserArray = (InstancingUserArray)instancing.Type;

                instancingUserArray.UpdateWorldMatrices(data.InstancingWorldMatrices.Items, data.InstancingWorldMatrices.Count);
            }
            else
            {
                model.Enabled = false;
                instancing.Enabled = false;
            }

            model.Enabled = Enabled;
            data.ImpostorEntity!.Get<ModelComponent>().Enabled = Enabled;
        }

        static bool IsDirty(VegetationComponent component, RuntimeData data)
            => data.PositionsBuffer == null 
            || data.InstancingEntity == null 
            || data.ImpostorMaterial != component.ImpostorMaterial 
            || data.Model != component.Model 
            || data.ImpostorSize != component.ImpostorSize;
    }

    private static bool InitializeRuntimeData(GraphicsDevice graphicsDevice, VegetationComponent component, RuntimeData data)
    {
        data.ImpostorMaterial = component.ImpostorMaterial;
        data.Model = component.Model;
        data.ImpostorSize = component.ImpostorSize;

        if (data.Model == null || data.ImpostorMaterial == null || string.IsNullOrEmpty(component.InstancesJson))
            return false;

        // Load instance data
        var instances = JsonSerializer.Deserialize<List<Vector4>>(component.InstancesJson, _jsonOptions)!;

        if (instances.Count == 0)
            return false;

        for (var i = 0; i < instances.Count; i++)
        {
            var scale = instances[i].W;
            float scaleX = data.ImpostorSize.X * scale;
            float scaleY = data.ImpostorSize.Y * scale;
            instances[i] = new(instances[i].X, instances[i].Y, instances[i].Z, PackScale(scaleX, scaleY));

            var gridPosition = GetGridPosition(instances[i].X, instances[i].Z);
            if (!data.GridPositions.ContainsKey(gridPosition))
                data.GridPositions.Add(gridPosition, []);

            data.GridPositions[gridPosition].Add(new(instances[i].X, instances[i].Y, instances[i].Z, scale));
        }

        data.PositionsBuffer = Buffer.New(graphicsDevice, (ReadOnlySpan<Vector4>)CollectionsMarshal.AsSpan(instances), BufferFlags.StructuredBuffer | BufferFlags.ShaderResource | BufferFlags.UnorderedAccess);

        // Setup impostor + model entities
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

        entity.AddChild(data.ImpostorEntity);

        data.InstancingEntity =
        [
            new ProfilingKeyComponent
            {
                ProfilingKey  = ProfilingKeyInstancedDraw
            },
            new ModelComponent
            {
                BoundingSphere = new(Vector3.Zero, 10000),
                BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
                Model = data.Model,
                Enabled = false,
                IsShadowCaster = true
            },
            new InstancingComponent
            {
                Enabled = false,
                Type = new InstancingUserArray
                {
                    WorldMatrices = []
                }
            }
        ];

        component.Entity.AddChild(data.InstancingEntity);

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

    public class RuntimeData : IDisposable
    {
        public Buffer? PositionsBuffer;
        public Entity? InstancingEntity;
        public Entity? ImpostorEntity;
        public Dictionary<(int, int), List<Vector4>> GridPositions = [];

        public Material? ImpostorMaterial;
        public Model? Model;

        public Vector2 ImpostorSize;

        public FastList<Matrix> InstancingWorldMatrices = new();

        public void Dispose()
        {
            PositionsBuffer?.Dispose();

            if (InstancingEntity != null)
            {
                InstancingEntity.SetParent(null);
                InstancingEntity.Scene = null;
                InstancingEntity = null;
            }

            if (ImpostorEntity != null)
            {
                ImpostorEntity.SetParent(null);
                ImpostorEntity.Scene = null;
                ImpostorEntity = null;
            }

            ImpostorMaterial = null;
            Model = null;
            GridPositions.Clear();
        }
    }

    class VegetationInstance
    {
        public float X;
        public float Y;
        public float Z;
        public float Scale;
    }
}
