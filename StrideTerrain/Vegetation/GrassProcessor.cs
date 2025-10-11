using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Rendering.Profiling;
using StrideTerrain.TerrainSystem;
using System;
using System.Collections.Generic;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.Vegetation;

public class GrassProcessor : EntityProcessor<GrassComponent, GrassProcessor.RuntimeData>
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Grass.Draw");

    private static float[] _grassDensities = [ 0, 0.2f, 0.4f, 0.5f, 0.6f, 0.8f, 1.0f ];

    protected override RuntimeData GenerateComponentData([NotNull] Entity entity, [NotNull] GrassComponent component)
        => new();

    protected override void OnEntityComponentAdding(Entity entity, [NotNull] GrassComponent component, [NotNull] RuntimeData data)
    {
        base.OnEntityComponentAdding(entity, component, data);
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] GrassComponent component, [NotNull] RuntimeData data)
    {
        base.OnEntityComponentRemoved(entity, component, data);

        if (data.Entity != null)
        {
            data.Dispose();
        }
    }

    public override void Update(GameTime time)
    {
        base.Update(time);

        var graphicsDevice = Services.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;
        var sceneSystem = Services.GetSafeServiceAs<SceneSystem>();

        var camera = sceneSystem.TryGetMainCamera();
        if (camera == null)
            return;

        var terrainProcessor = sceneSystem.SceneInstance.Processors.Get<TerrainProcessor>();
        if (terrainProcessor == null)
            return;

        var terrainData = terrainProcessor.TerrainData;
        if (terrainData?.MeshManager?.IsReady != true)
            return;

        var cameraPosition = camera.GetWorldPosition();

        var (_, lodLevalAtCamera) = terrainData.GetAtlasUv(cameraPosition.X, cameraPosition.Z);
        if (lodLevalAtCamera != 0)
            return;

        foreach (var componentData in ComponentDatas)
        {
            var component = componentData.Key;
            var data = componentData.Value;

            if (data.Entity == null || data.InstanceCount != component.InstanceCount)
            {
                data.Dispose();
                InitializeRuntimeData(graphicsDevice, component, data);
            }

            if (data.Entity == null)
                continue;

            var camChunkX = (int)MathF.Floor(cameraPosition.X / data.ChunkSize);
            var camChunkZ = (int)MathF.Floor(cameraPosition.Z / data.ChunkSize);

            if (data.CenterChunkCoord.X != camChunkX || data.CenterChunkCoord.Y != camChunkZ)
            {
                data.CenterChunkCoord = new Point(camChunkX, camChunkZ);
                RefreshChunks(terrainData, data);
            }
        }
    }

    private void RefreshChunks(TerrainRuntimeData terrainData, RuntimeData data)
    {
        var half = data.ChunkCount / 2;
        for (var z = 0; z < data.ChunkCount; z++)
        {
            for (var x = 0; x < data.ChunkCount; x++)
            {
                var worldChunkX = data.CenterChunkCoord.X + (x - half);
                var worldChunkZ = data.CenterChunkCoord.Y + (z - half);

                var chunk = data.Chunks[z * data.ChunkCount + x];

                // If this chunk’s coords are different, regenerate
                if (chunk.WorldCoord.X != worldChunkX || chunk.WorldCoord.Y != worldChunkZ)
                {
                    chunk.WorldCoord = new Point(worldChunkX, worldChunkZ);

                    FillGrassInstances(chunk, worldChunkX, worldChunkZ, terrainData, data);
                }
            }
        }
    }

    private void FillGrassInstances(Chunk chunk, int chunkX, int chunkZ, TerrainRuntimeData terrainData, RuntimeData data)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        // Define an overlap region along edges to avoid gaps
        float overlap = 2.0f; // meters (or world units)
        float generationSize = data.ChunkSize + overlap * 2;

        int instanceCount = 0;

        for (int i = 0; i < data.InstanceCount; i++)
        {
            // Seed RNG deterministically per world-space grid
            var cellX = chunkX * data.ChunkSize + i; // simple way to vary seed
            var cellZ = chunkZ * data.ChunkSize + i;
            var rng = new Random(HashCode.Combine(cellX, cellZ, data.Model!.GetHashCode(), i));

            // Random position within chunk + overlap
            float lx = (float)rng.NextDouble() * generationSize - overlap;
            float lz = (float)rng.NextDouble() * generationSize - overlap;
            float wx = chunkX * data.ChunkSize + lx;
            float wz = chunkZ * data.ChunkSize + lz;

            // Skip positions outside the actual chunk bounds
            if (wx < chunkX * data.ChunkSize || wx >= (chunkX + 1) * data.ChunkSize) continue;
            if (wz < chunkZ * data.ChunkSize || wz >= (chunkZ + 1) * data.ChunkSize) continue;

            // Sample terrain height
            var (uv, _) = terrainData.GetAtlasUv(wx, wz);
            float wy = terrainData.GetHeightAt(uv);

            // Sample control map
            var controlValue = terrainData.GetControlMapAt(uv);
            var backgroundTextureIndex = controlValue & 0x1F;
            bool isGrass = backgroundTextureIndex == 0 || backgroundTextureIndex == 4 ||
                           backgroundTextureIndex == 22 || backgroundTextureIndex == 27 || backgroundTextureIndex == 28;

            if (!isGrass) continue;

            // Random rotation and scale
            float scaleFactor = 0.5f + (float)rng.NextDouble() * 0.5f;
            var scale = new Vector3(scaleFactor);
            var rotation = Quaternion.RotationY((float)rng.NextDouble() * MathF.PI * 2f);

            var position = new Vector3(wx, wy, wz);
            float r = chunk.Model.BoundingSphere.Radius * scaleFactor;

            // Keep track of bounding box
            var min = position - new Vector3(r);
            var max = position + new Vector3(r);

            if (minX > maxX) // first valid instance
            {
                minX = min.X; minY = min.Y; minZ = min.Z;
                maxX = max.X; maxY = max.Y; maxZ = max.Z;
            }
            else
            {
                minX = MathF.Min(minX, min.X);
                minY = MathF.Min(minY, min.Y);
                minZ = MathF.Min(minZ, min.Z);
                maxX = MathF.Max(maxX, max.X);
                maxY = MathF.Max(maxY, max.Y);
                maxZ = MathF.Max(maxZ, max.Z);
            }

            // Compute matrices
            Matrix.Transformation(ref scale, ref rotation, ref position, out chunk.World[instanceCount]);
            chunk.InvWorld[instanceCount] = Matrix.Invert(chunk.World[instanceCount]);
            instanceCount++;
        }

        // Update GPU buffers
        var graphicsContext = Services.GetSafeServiceAs<GraphicsContext>();
        chunk.WorldBuffer.SetData(graphicsContext.CommandList, (ReadOnlySpan<Matrix>)chunk.World[..instanceCount]);
        chunk.InvWorldBuffer.SetData(graphicsContext.CommandList, (ReadOnlySpan<Matrix>)chunk.InvWorld[..instanceCount]);

        var instancing = (InstancingUserBuffer)chunk.Instancing.Type;
        instancing.InstanceCount = instanceCount;
        instancing.BoundingBox = new BoundingBox(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
    }

    private static void InitializeRuntimeData(GraphicsDevice graphicsDevice, GrassComponent component, RuntimeData data)
    {
        if (component.Model?.Model == null)
            return;

        data.InstanceCount = component.InstanceCount;
        data.Model = component.Model.Model;
        data.CenterChunkCoord = new(int.MaxValue, int.MaxValue);
        data.Chunks = new(data.ChunkCount * data.ChunkCount);

        component.Model.Enabled = false;

        data.Entity = [];
        component.Entity.Scene.Entities.Add(data.Entity);

        for (var z = 0; z < data.ChunkCount; z++)
        {
            for (var x = 0; x < data.ChunkCount; x++)
            {
                var instances = new Matrix[data.InstanceCount];
                var invInstances = new Matrix[data.InstanceCount];
                var worldBuffer = Buffer.New(graphicsDevice, (ReadOnlySpan<Matrix>)instances.AsSpan(), BufferFlags.ShaderResource | BufferFlags.StructuredBuffer);
                var invWorldBuffer = Buffer.New(graphicsDevice, (ReadOnlySpan<Matrix>)invInstances.AsSpan(), BufferFlags.ShaderResource | BufferFlags.StructuredBuffer);

                var entity = new Entity()
                        {
                            new ModelComponent
                            {
                                Model = data.Model,
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

                data.Entity.AddChild(entity);
                data.Chunks.Add(new()
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

    public class RuntimeData : IDisposable
    {
        public int ChunkCount = 3;
        public int ChunkSize = 32;
        public int InstanceCount;
        public Model? Model;

        public Entity? Entity;
        public List<Chunk> Chunks = [];
        public Point CenterChunkCoord;

        public void Dispose()
        {
            foreach (var chunk in Chunks)
            {
                chunk.Model.Entity.SetParent(null);
                
                var instancing = (InstancingUserBuffer)chunk.Instancing.Type;

                instancing.InstanceWorldBuffer.Dispose();
                instancing.InstanceWorldInverseBuffer.Dispose();

                instancing.InstanceWorldBuffer = null;
                instancing.InstanceWorldInverseBuffer = null;
            }

            Chunks.Clear();

            Entity?.Scene?.Entities?.Remove(Entity);
        }
    }

    public class Chunk
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
