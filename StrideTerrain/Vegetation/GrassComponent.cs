using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using StrideTerrain.TerrainSystem;
using System;
using System.Collections.Generic;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.Vegetation;

public class GrassComponent : SyncScript
{
    const int ChunkCount = 6;
    const int ChunkSize = 32;
    const int InstancesPerRow = 32;

    public InstancingComponent? Instancing { get; set; }
    public ModelComponent? Model { get; set; }

    private Buffer? _worldBuffer;
    private Buffer? _invWorldBuffer;

    private List<Chunk> _chunks = [];

    public override void Cancel()
    {
        base.Cancel();

        _chunks.Clear();

        _worldBuffer?.Dispose();
        _invWorldBuffer?.Dispose();
    }

    public override void Start()
    {
        base.Start();

        if (Model == null || Instancing == null)
            return;

        // Setup instancing data
        float s = ChunkSize / (float)InstancesPerRow;
        var worldMatrices = new Matrix[InstancesPerRow * InstancesPerRow];
        var invWorldMatrix = new Matrix[InstancesPerRow * InstancesPerRow];
        for (var z = 0; z < InstancesPerRow; z++)
        {
            for (var x = 0; x < InstancesPerRow; x++)
            {
                var position = new Vector3(x * s - ChunkSize / 2, 0, z * s - ChunkSize / 2);
                var index = z * InstancesPerRow + x;
                worldMatrices[index] = Matrix.Translation(position);
                Matrix.Invert(ref worldMatrices[index], out invWorldMatrix[index]);
            }
        }

        _worldBuffer = Buffer.New(GraphicsDevice, (ReadOnlySpan<Matrix>)worldMatrices.AsSpan(), BufferFlags.ShaderResource | BufferFlags.StructuredBuffer);
        _invWorldBuffer = Buffer.New(GraphicsDevice, (ReadOnlySpan<Matrix>)invWorldMatrix.AsSpan(), BufferFlags.ShaderResource | BufferFlags.StructuredBuffer);

        Model.Enabled = false;
        Instancing.Enabled = false;

        // Create child chunks
        for (var z = 0; z < ChunkCount; z++)
        {
            for (var x = 0; x < ChunkCount; x++)
            {
                var entity = new Entity()
                {
                    new ModelComponent
                    {
                        Model = Model.Model,
                        IsShadowCaster = false
                    },
                    new InstancingComponent
                    {
                        Type = new InstancingUserBuffer
                        {
                            InstanceWorldBuffer = _worldBuffer,
                            InstanceWorldInverseBuffer = _invWorldBuffer,
                            InstanceCount = worldMatrices.Length,
                            BoundingBox = new BoundingBox(new(-8000, -400, -8000), new(8000, 400, 8000)) // TODO I guess
                        }
                    }
                };

                Entity.AddChild(entity);
                _chunks.Add(new()
                {
                    Transform = entity.Transform,
                    Model = entity.Get<ModelComponent>(),
                    Instancing = entity.Get<InstancingComponent>()
                });
            }
        }
    }

    public override void Update()
    {
        if (Instancing == null || Model == null)
            return;

        var camera = SceneSystem.TryGetMainCamera();
        if (camera == null)
            return;

        var terrainProcessor = SceneSystem.SceneInstance.Processors.Get<TerrainProcessor>();
        if (terrainProcessor == null)
            return;

        foreach (var material in Model.Model.Materials)
        {
            foreach (var pass in material.Material.Passes)
            {
                if (!terrainProcessor.SetMaterialParameters(pass.Parameters))
                    return;
            }
        }

        var position = camera.GetWorldPosition();
        position.Y = 0;

        var px = (int)position.X;
        var pz = (int)position.Z;

        position.X = px - (px % ChunkSize) + ChunkSize / 2;
        position.Z = pz - (pz % ChunkSize) + ChunkSize / 2;

        position.X -= ChunkSize * ChunkCount / 2;
        position.Z -= ChunkSize * ChunkCount / 2;

        for (var z = 0; z < ChunkCount; z++)
        {
            for (var x = 0; x < ChunkCount; x++)
            {
                var chunkIndex = z * ChunkCount + x;
                _chunks[chunkIndex].Transform.Position = position + new Vector3(x * ChunkSize, 0, z * ChunkSize);
                _chunks[chunkIndex].Transform.UpdateWorldMatrix();
            }
        }
    }

    private class Chunk
    {
        public required TransformComponent Transform;
        public required ModelComponent Model;
        public required InstancingComponent Instancing;
    }
}
