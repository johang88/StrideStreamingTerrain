using Stride.Core;
using Stride.Core.Collections;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using StrideTerrain.Common;
using StrideTerrain.Rendering.Profiling;
using StrideTerrain.TerrainSystem;
using StrideTerrain.Vegetation.Effects;
using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.Vegetation;

#pragma warning disable CS0618 // Type or member is obsolete
public class GrassComponent : SyncScript
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Grass.Draw");

    const int ChunkCount = 6;
    const int ChunkSize = 32;

    public InstancingComponent? Instancing { get; set; }
    public ModelComponent? Model { get; set; }
    public int InstanceCount { get; set; } = 32 * 32;

    private List<Chunk> _chunks = new(ChunkCount * ChunkCount);
    private Point _centerChunkCoord;

    public override void Cancel()
    {
        base.Cancel();

        _chunks.Clear();
    }

    public override void Start()
    {
        base.Start();

        if (Model == null || Instancing == null)
            return;

        // Setup instancing data
        Model.Enabled = false;
        Instancing.Enabled = false;

        // Create child chunks
        for (var z = 0; z < ChunkCount; z++)
        {
            for (var x = 0; x < ChunkCount; x++)
            {
                var instances = new Matrix[InstanceCount];
                var invInstances = new Matrix[InstanceCount];
                var worldBuffer = Buffer.New(GraphicsDevice, (ReadOnlySpan<Matrix>)instances.AsSpan(), BufferFlags.ShaderResource | BufferFlags.StructuredBuffer);
                var invWorldBuffer = Buffer.New(GraphicsDevice, (ReadOnlySpan<Matrix>)invInstances.AsSpan(), BufferFlags.ShaderResource | BufferFlags.StructuredBuffer);

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
                            InstanceWorldBuffer = worldBuffer,
                            InstanceWorldInverseBuffer = invWorldBuffer,
                            InstanceCount = 0,
                            BoundingBox = new BoundingBox(new(-8000, -400, -8000), new(8000, 400, 8000)) // TODO I guess
                        }
                    },
                    new ProfilingKeyComponent
                    {
                        ProfilingKey = ProfilingKeyDraw
                    }
                };

                Entity.AddChild(entity);
                _chunks.Add(new()
                {
                    Model = entity.Get<ModelComponent>(),
                    Instancing = entity.Get<InstancingComponent>(),
                    World = instances,
                    InvWorld = invInstances,
                    WorldBuffer = worldBuffer,
                    InvWorldBuffer = invWorldBuffer
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

        var terrainData = terrainProcessor.TerrainData;
        if (terrainData?.MeshManager?.IsReady != true)
            return;

        var cameraPosition = camera.GetWorldPosition();

        var (_, lodLevalAtCamera) = terrainData.GetAtlasUv(cameraPosition.X, cameraPosition.Z);
        if (lodLevalAtCamera != 0)
            return;

        var camChunkX = (int)MathF.Floor(cameraPosition.X / ChunkSize);
        var camChunkZ = (int)MathF.Floor(cameraPosition.Z / ChunkSize);

        if (_centerChunkCoord.X != camChunkX || _centerChunkCoord.Y != camChunkZ)
        {
            _centerChunkCoord = new Point(camChunkX, camChunkZ);
            RefreshChunks(terrainData);
        }
    }

    private void RefreshChunks(TerrainRuntimeData terrainData)
    {
        int half = ChunkCount / 2;

        for (int z = 0; z < ChunkCount; z++)
        {
            for (int x = 0; x < ChunkCount; x++)
            {
                int worldChunkX = _centerChunkCoord.X + (x - half);
                int worldChunkZ = _centerChunkCoord.Y + (z - half);

                var chunk = _chunks[z * ChunkCount + x];

                // If this chunk’s coords are different, regenerate
                if (chunk.WorldCoord.X != worldChunkX || chunk.WorldCoord.Y != worldChunkZ)
                {
                    chunk.WorldCoord = new Point(worldChunkX, worldChunkZ);

                    FillGrassInstances(chunk, worldChunkX, worldChunkZ, terrainData);
                }
            }
        }
    }

    private void FillGrassInstances(Chunk chunk, int chunkX, int chunkZ, TerrainRuntimeData terrainData)
    {
        var rng = new Random(HashCode.Combine(chunkX, chunkZ, Model!.GetHashCode()));
        var instanceCount = 0;
        for (var i = 0; i < InstanceCount; i++)
        {
            float lx = (float)rng.NextDouble() * ChunkSize;
            float lz = (float)rng.NextDouble() * ChunkSize;
            float wx = chunkX * ChunkSize + lx;
            float wz = chunkZ * ChunkSize + lz;

            float scaleFactor = 0.8f + (float)rng.NextDouble() * 0.3f;
            var scale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
            var rotation = Quaternion.RotationY((float)rng.NextDouble() * MathF.PI * 2.0f);
            var position = new Vector3(wx, 0, wz);

            var (uv, _) = terrainData.GetAtlasUv(position.X, position.Z);
            position.Y = terrainData.GetHeightAt(uv);

            var controlValue = terrainData.GetControlMapAt(uv);
            var backgroundTextureIndex = ((controlValue >> 5) & 0x1F) - 1;

            var isGrass = (backgroundTextureIndex == 0 || backgroundTextureIndex == 4 || backgroundTextureIndex == 22 || backgroundTextureIndex == 27 || backgroundTextureIndex == 28);

            if (!isGrass)
                continue;

            // TODO: We could actually compute bounds here ...

            Matrix.Transformation(ref scale, ref rotation, ref position, out chunk.World[i]);
            chunk.InvWorld[i] = Matrix.Invert(chunk.World[i]);
            instanceCount++;
        }

        var graphicsContext = Services.GetSafeServiceAs<GraphicsContext>();
        chunk.WorldBuffer.SetData(graphicsContext.CommandList, (ReadOnlySpan<Matrix>)chunk.World);
        chunk.InvWorldBuffer.SetData(graphicsContext.CommandList, (ReadOnlySpan<Matrix>)chunk.InvWorld);

        ((InstancingUserBuffer)chunk.Instancing.Type).InstanceCount = instanceCount;
    }

    private class Chunk
    {
        public Point WorldCoord;
        public required ModelComponent Model;
        public required InstancingComponent Instancing;
        public required Matrix[] World;
        public required Matrix[] InvWorld;
        public required Buffer WorldBuffer;
        public required Buffer InvWorldBuffer;
    }
}
#pragma warning restore CS0618 // Type or member is obsolete