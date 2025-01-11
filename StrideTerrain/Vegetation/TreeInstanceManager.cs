using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Engine;
using StrideTerrain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace StrideTerrain.Vegetation;

public class TreeInstanceManager : StartupScript
{
    public List<InstancingComponent> Models { get; set; } = [];
    public UrlReference? TreeData { get; set; }

    public override void Start()
    {
        base.Start();

        if (TreeData == null)
            return;

        using var stream = Content.OpenAsStream(TreeData.Url, StreamFlags.None);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            IncludeFields = true
        };
        var trees = JsonSerializer.Deserialize<List<TreeInstance>>(stream, options)!;

        var matrices = Models.Select(x => new List<Matrix>(trees.Count)).ToList();

        foreach (var tree in trees)
        {
            var i = Random.Shared.Next(0, Models.Count);
            var scale = 0.8f + Random.Shared.NextSingle() * 1.8f;
            var rotation = Random.Shared.NextSingle() * 3.14f * 2.0f;
            var matrix = Matrix.Scaling(scale) * Matrix.RotationY(rotation) * Matrix.Translation(tree.X, tree.Y, tree.Z);
            matrices[i].Add(matrix);
        }

        for (var i = 0; i < Models.Count; i++)
        {
            (Models[i].Type as InstancingUserArray)!.UpdateWorldMatrices([.. matrices[i]]);
        }
    }
}
