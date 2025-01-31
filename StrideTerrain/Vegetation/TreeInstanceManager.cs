using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Engine;
using Stride.Graphics;
using StrideTerrain.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System;
using Buffer = Stride.Graphics.Buffer;
using Stride.Core.Diagnostics;
using StrideTerrain.Rendering;

namespace StrideTerrain.Vegetation;

public class TreeInstanceManager : StartupScript
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Trees.Draw");

    private static JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true
    };

    public List<InstancingComponent> Models { get; set; } = [];
    public UrlReference? TreeData { get; set; }

    private List<InstancingUserBuffer> _instancingBuffers = [];

    public override void Cancel()
    {
        base.Cancel();

        foreach (var buffer in _instancingBuffers)
        {
            buffer.InstanceWorldBuffer?.Dispose();
            buffer.InstanceWorldInverseBuffer?.Dispose();
        }
        _instancingBuffers.Clear();
    }

    public override void Start()
    {
        base.Start();

        if (TreeData == null)
            return;

        using var stream = Content.OpenAsStream(TreeData.Url, StreamFlags.None);

        var trees = JsonSerializer.Deserialize<List<TreeInstance>>(stream, _jsonOptions)!;

        foreach (var model in Models)
        {
            model.Entity.Get<ModelComponent>().IsShadowCaster = false;
            model.Entity.Add(new ProfilingKeyComponent
            {
                ProfilingKey = ProfilingKeyDraw
            });
        }

        // Not very nice now is it ...but it will work ...
        var matrices = Models.Select(x => new List<Matrix>(trees.Count)).ToList();
        var inverseMatrices = Models.Select(x => new List<Matrix>(trees.Count)).ToList();

        foreach (var tree in trees)
        {
            // TMP: As everything else in this class ...
            if (tree.X < 3000 || tree.Z < 3000 || tree.X > 5000 || tree.Z > 5000)
                continue;

            var i = Random.Shared.Next(0, Models.Count);
            var scale = 0.8f + Random.Shared.NextSingle() * 1.8f;
            var rotation = Random.Shared.NextSingle() * 3.14f * 2.0f;
            var matrix = Matrix.Scaling(scale) * Matrix.RotationY(rotation) * Matrix.Translation(tree.X, tree.Y, tree.Z);
            matrices[i].Add(matrix);
            inverseMatrices[i].Add(Matrix.Invert(matrix));
        }

        var totalCount = matrices.Select(x => x.Count).Sum();

        for (var i = 0; i < Models.Count; i++)
        {
            ReadOnlySpan<Matrix> worldMatrices = CollectionsMarshal.AsSpan(matrices[i]);
            ReadOnlySpan<Matrix>  inverseWorldMatrices = CollectionsMarshal.AsSpan(inverseMatrices[i]);

            var instancingBuffer = new InstancingUserBuffer
            {
                InstanceWorldBuffer = Buffer.New(GraphicsDevice, worldMatrices, BufferFlags.ShaderResource | BufferFlags.StructuredBuffer),
                InstanceWorldInverseBuffer = Buffer.New(GraphicsDevice, worldMatrices, BufferFlags.ShaderResource | BufferFlags.StructuredBuffer),
                InstanceCount = worldMatrices.Length,
                BoundingBox = new BoundingBox(new(-8000, -400, -8000), new(8000, 400, 8000)) // TODO I guess
            };

            _instancingBuffers.Add(instancingBuffer);
            Models[i].Type = instancingBuffer;
        }
    }

    class TreeInstance
    {
        public float X;
        public float Y;
        public float Z;
    }
}
