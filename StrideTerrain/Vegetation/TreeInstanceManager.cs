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
using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace StrideTerrain.Vegetation;

[DataContract]
public class TreeData
{ 
    public required Material Material { get; set; }
    public Vector2 Size { get; set; }
}

public class TreeInstanceManager : SyncScript
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Trees.Draw");

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true
    };

    public UrlReference? TreeData { get; set; }
    public List<TreeData> TreeTypes { get; set; } = [];

    private List<Buffer> _buffers = [];
    private List<ModelComponent> _models = [];

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

        List<List<Vector4>> treeInstances = [.. TreeTypes.Select(x => new List<Vector4>(trees.Count))];
        for (var i = 0; i < trees.Count; i++)
        {
            var tree = trees[i];
            var scale = 1.3f + Random.Shared.NextSingle() * 3.6f;

            var treeType = Random.Shared.Next(TreeTypes.Count);
            float scaleX = TreeTypes[treeType].Size.X * scale;
            float scaleY = TreeTypes[treeType].Size.Y * scale;
            
            treeInstances[treeType].Add(new(tree.X, tree.Y, tree.Z, PackScale(scaleX, scaleY)));
        }

        for (var i = 0; i < treeInstances.Count; i++)
        {
            var entity = new Entity();

            var model = entity.GetOrCreate<ModelComponent>();
            model.Model ??= [new Mesh()
            {
                Draw = new MeshDraw
                {
                    PrimitiveType = PrimitiveType.TriangleList,
                    VertexBuffers = [],
                    DrawCount = treeInstances[i].Count * 6
                },
                BoundingBox = new BoundingBox(new Vector3(-100000, -100000, -100000), new Vector3(100000, 100000, 100000)),
            }];
            model.Model.BoundingSphere = new(Vector3.Zero, 10000);
            model.Model.BoundingBox = BoundingBox.FromSphere(model.BoundingSphere);
            model.IsShadowCaster = false;
            model.Materials[0] = TreeTypes[i].Material;

            var buffer = Buffer.Structured.New(GraphicsDevice, treeInstances[i].ToArray(), true);

            _buffers.Add(buffer);
            _models.Add(model);

            Entity.AddChild(entity);
        }

        static float PackScale(float scaleX, float scaleY)
        {
            // convert to half-floats
            Half hx = (Half)scaleX;
            Half hy = (Half)scaleY;

            // pack into a 32-bit uint: low 16 bits = X, high 16 bits = Y
            uint packed = ((uint)BitConverter.HalfToUInt16Bits(hy) << 16) | BitConverter.HalfToUInt16Bits(hx);

            // reinterpret as float
            return BitConverter.Int32BitsToSingle((int)packed);
        }
    }

    public override void Update()
    {
        for (var i = 0 ; i < _models.Count; i++)
        {
            _models[i].Materials[0].Passes[0].Parameters.Set(MaterialImpostorDisplacementFeatureKeys.Positions, _buffers[i]);
        }
    }

    class TreeInstance
    {
        public float X;
        public float Y;
        public float Z;
    }
}
