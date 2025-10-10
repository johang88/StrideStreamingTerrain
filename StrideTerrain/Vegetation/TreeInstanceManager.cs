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

namespace StrideTerrain.Vegetation;

[DataContract]
public class TreeData
{
    public required Material Material { get; set; }
    public required Model Model { get; set; }
    public Vector2 Size { get; set; }
    public float LodDistance { get; set; } = 64.0f;
}

#pragma warning disable CS0618 // Type or member is obsolete
public class TreeInstanceManager : SyncScript
{
    private static readonly ProfilingKey ProfilingKeyImpostorsDraw = new("Trees.Draw.Impostors");
    private static readonly ProfilingKey ProfilingKeyInstancedDraw = new("Trees.Draw.Instanced");

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true
    };

    public UrlReference? TreeData { get; set; }
    public List<TreeData> TreeTypes { get; set; } = [];

    private List<RuntimeData> _runtimeData = [];

    private const int GridSize = 128;

    public override void Cancel()
    {
        base.Cancel();
    }

    public override void Start()
    {
        base.Start();

        if (TreeData == null || TreeTypes.Count == 0)
            return;

        using var stream = Content.OpenAsStream(TreeData.Url, StreamFlags.None);

        var trees = JsonSerializer.Deserialize<List<TreeInstance>>(stream, _jsonOptions)!;

        var rng = new Random(1337);
        List<Dictionary<(int, int), List<Vector4>>> gridPositions = [.. TreeTypes.Select(x => new Dictionary<(int, int), List<Vector4>>())];
        List<List<Vector4>> treeInstances = [.. TreeTypes.Select(x => new List<Vector4>(trees.Count))];
        for (var i = 0; i < trees.Count; i++)
        {
            var tree = trees[i];
            var scale = 1.0f;

            float scaleX = TreeTypes[tree.Type].Size.X * scale;
            float scaleY = TreeTypes[tree.Type].Size.Y * scale;

            treeInstances[tree.Type].Add(new(tree.X, tree.Y, tree.Z, PackScale(scaleX, scaleY)));
            var gridPosition = GetGridPosition(tree.X, tree.Z);
            if (!gridPositions[tree.Type].ContainsKey(gridPosition))
                gridPositions[tree.Type].Add(gridPosition, []);
            gridPositions[tree.Type][gridPosition].Add(new(tree.X, tree.Y, tree.Z, scale));
        }

        for (var i = 0; i < treeInstances.Count; i++)
        {
            // Setup impostor data
            var impostorEntity = new Entity
            {
                new ProfilingKeyComponent
                {
                    ProfilingKey  = ProfilingKeyImpostorsDraw
                }
            };

            var impostorModel = impostorEntity.GetOrCreate<ModelComponent>();
            impostorModel.Model ??= [new Mesh()
            {
                Draw = new MeshDraw
                {
                    PrimitiveType = PrimitiveType.TriangleList,
                    VertexBuffers = [],
                    DrawCount = treeInstances[i].Count * 6,
                },
                BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
            }];
            impostorModel.RenderGroup = RenderGroups.Impostors;
            impostorModel.Model.BoundingSphere = new(Vector3.Zero, 10000);
            impostorModel.Model.BoundingBox = BoundingBox.FromSphere(impostorModel.BoundingSphere);
            impostorModel.IsShadowCaster = false;
            impostorModel.Materials[0] = TreeTypes[i].Material;

            var buffer = Buffer.Structured.New(GraphicsDevice, treeInstances[i].ToArray(), true);

            Entity.AddChild(impostorEntity);

            // Setup instancing data
            var instancingEntity = new Entity
            {
                new ProfilingKeyComponent
                {
                    ProfilingKey  = ProfilingKeyInstancedDraw
                },
                new ModelComponent
                {
                    BoundingSphere = new(Vector3.Zero, 10000),
                    BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
                    Model = TreeTypes[i].Model,
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
            };

            Entity.AddChild(instancingEntity);

            _runtimeData.Add(new()
            {
                PositionsBuffer = buffer,
                ImpostorMaterial = impostorModel.Materials[0],
                TreeData = TreeTypes[i],
                InstancingEntity = instancingEntity,
                Positions = gridPositions[i]
            });
        }

        static float PackScale(float scaleX, float scaleY)
        {
            var hx = (Half)scaleX;
            var hy = (Half)scaleY;

            uint packed = ((uint)BitConverter.HalfToUInt16Bits(hy) << 16) | BitConverter.HalfToUInt16Bits(hx);

            return BitConverter.Int32BitsToSingle((int)packed);
        }
    }

    public override void Update()
    {
        var camera = SceneSystem.TryGetMainCamera();
        if (camera == null) 
            return;

        var cameraPosition = camera.GetWorldPosition();

        foreach (var runtimeData in _runtimeData)
        {
            runtimeData.ImpostorMaterial.Passes[0].Parameters.Set(MaterialImpostorDisplacementFeatureKeys.Positions, runtimeData.PositionsBuffer);
            runtimeData.ImpostorMaterial.Passes[0].Parameters.Set(MaterialImpostorDisplacementFeatureKeys.LodDistance, runtimeData.TreeData.LodDistance);

            runtimeData.InstancingWorldMatrices.Clear();
            var lodDistance = runtimeData.TreeData.LodDistance;
            var lodDistanceSquared = lodDistance * lodDistance;
            var start = GetGridPosition(cameraPosition.X - lodDistance, cameraPosition.Z - lodDistance);
            var end = GetGridPosition(cameraPosition.X + lodDistance, cameraPosition.Z + lodDistance);
            for (var z = start.Z; z <= end.Z; z++)
            {
                for (var x = start.X; x <= end.X; x++)
                {
                    if (!runtimeData.Positions.TryGetValue((x, z), out var positions))
                        continue;

                    for (var i = 0; i < positions.Count; i++)
                    {
                        var distanceSquared = (cameraPosition.XZ() - new Vector2(positions[i].X, positions[i].Z)).LengthSquared();
                        if (distanceSquared < lodDistanceSquared)
                        {
                            runtimeData.InstancingWorldMatrices.Add(Matrix.Scaling(positions[i].W) * Matrix.Translation(positions[i].XYZ()));
                        }
                    }
                }
            }

            var model = runtimeData.InstancingEntity.Get<ModelComponent>();
            var instancing = runtimeData.InstancingEntity.Get<InstancingComponent>();
            
            if (runtimeData.InstancingWorldMatrices.Count > 0)
            {
                model.Enabled = true;
                instancing.Enabled = true;

                var instancingUserArray = (InstancingUserArray)instancing.Type;
                instancingUserArray.UpdateWorldMatrices(runtimeData.InstancingWorldMatrices.Items, runtimeData.InstancingWorldMatrices.Count);
            }
            else
            {
                model.Enabled = false;
                instancing.Enabled = false;
            }
        }
    }

    static (int X, int Z) GetGridPosition(float x, float z)
            => ((int)(x / GridSize), (int)(z / GridSize));

    class TreeInstance
    {
        public float X;
        public float Y;
        public float Z;
        public int Type;
    }

    class RuntimeData
    {
        public required Buffer PositionsBuffer;
        public required Material ImpostorMaterial;
        public required TreeData TreeData;
        public required Dictionary<(int, int), List<Vector4>> Positions;
        public required Entity InstancingEntity;
        public FastList<Matrix> InstancingWorldMatrices = new();
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
