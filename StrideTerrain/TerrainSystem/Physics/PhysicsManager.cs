using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Physics;
using StrideTerrain.TerrainSystem.Streaming;
using System;
using System.Buffers;

namespace StrideTerrain.TerrainSystem.Physics;

/// <summary>
/// Streams in terrain collider data (always lod0) centered around the player(camera).
/// </summary>
public sealed class PhysicsManager : IDisposable
{
    // TODO: This should be configurable
    private const int PhysicsEntityCount = 7;

    private readonly TerrainRuntimeData _terrain;
    private readonly IStreamingManager _streamingManager;
    private readonly Scene _scene;
    private readonly PhysicsEntity[] _physicsEntities;

    public PhysicsManager(TerrainRuntimeData terrain, Scene scene, IStreamingManager streamingManager)
    {
        _terrain = terrain;
        _scene = scene;
        _streamingManager = streamingManager;

        // Setup physics entities
        _physicsEntities = new PhysicsEntity[PhysicsEntityCount * PhysicsEntityCount];
        for (var i = 0; i < _physicsEntities.Length; i++)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var heightData = new UnmanagedArray<float>(_terrain.TerrainData.Header.ChunkTextureSize * _terrain.TerrainData.Header.ChunkTextureSize);
#pragma warning restore CS0618 // Type or member is obsolete

            var colliderShape = new HeightfieldColliderShape(_terrain.TerrainData.Header.ChunkTextureSize, _terrain.TerrainData.Header.ChunkTextureSize, heightData, 1.0f, 0.0f, _terrain.TerrainData.Header.MaxHeight, false)
            {
            };

            var collider = new StaticColliderComponent
            {
                ColliderShape = colliderShape
            };

            Entity entity = [collider];

            entity.Transform.Scale = new Vector3(_terrain.UnitsPerTexel, 1, _terrain.UnitsPerTexel);
            entity.Transform.Position = new Vector3(0, -100000, 0);
            _scene.Entities.Add(entity);

            _physicsEntities[i] = new PhysicsEntity
            {
                ChunkIndex = -1,
                DesiredChunkIndex = -1,
                DesiredChunkPosition = Vector3.Zero,
                Entity = entity,
                Collider = collider,
                ColliderShape = colliderShape
            };
        }
    }

    public void Dispose()
    {
        foreach (var physicsEntity in _physicsEntities)
        {
            physicsEntity.Entity.Scene = null;
        }
    }

    public void Update(float cameraPositionX, float cameraPositionZ)
    {
        // Transform camera position to terrain space.
        var cameraPositionXTerrainSpace = cameraPositionX / _terrain.UnitsPerTexel;
        var cameraPositionZTerrainSpace = cameraPositionZ / _terrain.UnitsPerTexel;

        // Calculate camera chunk position
        var cameraChunkX = (int)(cameraPositionXTerrainSpace / _terrain.TerrainData.Header.ChunkSize);
        var cameraChunkY = (int)(cameraPositionZTerrainSpace / _terrain.TerrainData.Header.ChunkSize);

        // Calculate desired chunk positions, note this assumes that the terrain is big enough to contain all physics chunks ...
        var halfChunkCount = PhysicsEntityCount / 2;
        var startX = Math.Max(0, cameraChunkX - halfChunkCount);
        var startY = Math.Max(0, cameraChunkY - halfChunkCount);

        var endX = startX + PhysicsEntityCount;
        var endY = startY + PhysicsEntityCount;

        if (endX > _terrain.ChunksPerRowLod0)
        {
            endX = _terrain.ChunksPerRowLod0;
            startX = endX - PhysicsEntityCount;
        }

        if (endY > _terrain.ChunksPerRowLod0)
        {
            endY = _terrain.ChunksPerRowLod0;
            startY = endY - PhysicsEntityCount;
        }

        // Update chunks.
        // The streaming logic could be improved a bit but it's good and simple enough for now.
        for (var y = 0; y < PhysicsEntityCount; y++)
        {
            for (var x = 0; x < PhysicsEntityCount; x++)
            {
                var chunkX = startX + x;
                var chunkY = startY + y;
                var chunkIndex = chunkY * _terrain.ChunksPerRowLod0 + chunkX;
                var chunkDataIndex = _terrain.TerrainData.GetChunkIndex(0, chunkIndex);

                var entityIndex = y * PhysicsEntityCount + x;
                var physicsEntity = _physicsEntities[entityIndex];

                // Request streaming if needed.
                if (physicsEntity.ChunkIndex != chunkDataIndex && physicsEntity.DesiredChunkIndex != chunkDataIndex)
                {
                    _physicsEntities[entityIndex].DesiredChunkIndex = chunkDataIndex;

                    var chunkOffset = _terrain.TerrainData.Header.ChunkSize;
                    _physicsEntities[entityIndex].DesiredChunkPosition = new Vector3(chunkX * chunkOffset + chunkOffset * 0.5f, 0, chunkY * chunkOffset + chunkOffset * 0.5f) * _terrain.UnitsPerTexel;

                    _streamingManager.Request(ChunksToLoad.Heightmap, chunkDataIndex, StreamingCompletedCallback, _physicsEntities[entityIndex]);
                }
            }
        }
    }

    static float ConvertToFloatHeight(float minValue, float maxValue, float value)
        => MathUtil.InverseLerp(minValue, maxValue, MathUtil.Clamp(value, minValue, maxValue));

    private void StreamingCompletedCallback(IStreamingRequest streamingRequest, object? callbackData)
    {
        var physicsEntity = (PhysicsEntity?)callbackData;
        if (physicsEntity == null)
            return; // Can't happen.

        // We changed our mind ... discard.
        if (physicsEntity.DesiredChunkIndex != streamingRequest.ChunkIndex)
            return;

        if (!streamingRequest.TryGetHeightmap(out var buffer))
            return; // Can't happen.

        // ushort -> float, the ushort support in bullet did not match for some reason .. or maybe I'm just lazy.
        var heightmap = ArrayPool<float>.Shared.Rent(_terrain.TerrainData.Header.ChunkTextureSize * _terrain.TerrainData.Header.ChunkTextureSize);
        for (var y = 0; y < _terrain.TerrainData.Header.ChunkTextureSize; y++)
        {
            for (var x = 0; x < _terrain.TerrainData.Header.ChunkTextureSize; x++)
            {
                var index = y * _terrain.TerrainData.Header.ChunkTextureSize + x;
                var height = BitConverter.ToUInt16(buffer.Slice(index * 2, 2));
                heightmap[index] = ConvertToFloatHeight(0, ushort.MaxValue, height) * _terrain.TerrainData.Header.MaxHeight;
            }
        }

        // Update collider information
        using (physicsEntity.ColliderShape.LockToReadAndWriteHeights())
        {
            physicsEntity.ColliderShape.FloatArray.Write(heightmap, 0, 0, _terrain.TerrainData.Header.ChunkTextureSize * _terrain.TerrainData.Header.ChunkTextureSize);
        }

        ArrayPool<float>.Shared.Return(heightmap);

        // Update transform
        physicsEntity.Entity.Transform.Scale = new Vector3(_terrain.UnitsPerTexel, 1, _terrain.UnitsPerTexel);
        physicsEntity.Entity.Transform.Position = physicsEntity.DesiredChunkPosition + new Vector3(0, _terrain.TerrainData.Header.MaxHeight * 0.5f, 0);
        physicsEntity.Collider.UpdatePhysicsTransformation();

        physicsEntity.ChunkIndex = physicsEntity.DesiredChunkIndex;
    }

    private sealed class PhysicsEntity
    {
        public int ChunkIndex;
        public int DesiredChunkIndex;
        public Vector3 DesiredChunkPosition;
        public required Entity Entity;
        public required StaticColliderComponent Collider;
        public required HeightfieldColliderShape ColliderShape;
    }
}
