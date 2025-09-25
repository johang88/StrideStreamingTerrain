using Stride.Core;
using Stride.Engine;
using Stride.Rendering;
using StrideTerrain.Common;
using StrideTerrain.TerrainSystem.Physics;
using StrideTerrain.TerrainSystem.Rendering;
using StrideTerrain.TerrainSystem.Streaming;
using System;

namespace StrideTerrain.TerrainSystem;

[DataContract]
public sealed class TerrainRuntimeData : IDisposable
{
    public const int RuntimeTextureSize = 4096;
    public const float InvRuntimeTextureSize = 1.0f / RuntimeTextureSize;
    public const int ShadowMapSize = 4096;

    public ITerrainDataProvider? DataProvider;
    public IStreamingManager? StreamingManager;
    public PhysicsManager? PhysicsManager;
    public GpuTextureManager? GpuTextureManager;
    public MeshManager? MeshManager;

    public float UnitsPerTexel => TerrainData.Header.UnitsPerTexel;
    public float Lod0Distance;
    public int MaximumLod;
    public int MinimumLod;
    public int ChunksPerRowLod0;

    public RenderModel? RenderModel;
    public ModelComponent? ModelComponent;

    public string? TerrainDataUrl;
    public TerrainData TerrainData;
    public bool IsInitialized;

    public void Dispose()
    {
        RenderModel = null;

        ModelComponent?.Entity?.Remove(ModelComponent);
        ModelComponent = null;

        StreamingManager?.Dispose();
        StreamingManager = null;

        PhysicsManager?.Dispose();
        PhysicsManager = null;

        GpuTextureManager?.Dispose();
        GpuTextureManager = null;
    }
}

public enum ChunkType
{
    Heightmap,
    NormalMap
}
