using SharpFont;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Common;
using StrideTerrain.TerrainSystem.Physics;
using StrideTerrain.TerrainSystem.Rendering;
using StrideTerrain.TerrainSystem.Streaming;
using System;
using System.Collections.Generic;
using System.IO;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.TerrainSystem;

[DataContract]
public sealed class TerrainRuntimeData : IDisposable
{
    public const int RuntimeTextureSize = 2048;
    public const float InvRuntimeTextureSize = 1.0f / RuntimeTextureSize;
    public const int ShadowMapSize = 4096;

    public ITerrainDataProvider? DataProvider;
    public StreamingManager? StreamingManager;
    public PhysicsManager? PhysicsManager;
    public GpuTextureManager? GpuTextureManager;

    // Minimum viable mesh, no buffers bound as all triangles are generated in the shader.
    public Mesh Mesh = new()
    {
        Draw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            VertexBuffers = []
        },
        BoundingBox = new BoundingBox(new Vector3(-10000, -10000, -10000), new Vector3(10000, 10000, 10000)),
    };

    public float UnitsPerTexel;

    public float Lod0Distance;

    public int MaximumLod;

    public int MinimumLod;

    public bool IsInitialized;

    public RenderModel? RenderModel;

    public ModelComponent? ModelComponent;

    public Vector3 CameraPosition;

    public ChunkData[] ChunkData = [];

    public int ChunkCount = 0;

    public int[] SectorToChunkMap = [];

    public Buffer? ChunkBuffer;

    public Buffer? SectorToChunkMapBuffer;

    public Buffer? ChunkInstanceData;

    public int ChunksPerRowLod0;

    public string? TerrainDataUrl;

    public TerrainData TerrainData;

    // Streaming data 
    public string? TerrainStreamDataUrl;

    public long BaseOffset;

    public Stream? TerrainStream;

    public Texture? ShadowMap;

    public void Dispose()
    {
        SectorToChunkMapBuffer?.Dispose();
        SectorToChunkMapBuffer = null;

        TerrainStream?.Dispose();
        TerrainStream = null;

        ShadowMap?.Dispose();
        ShadowMap = null;

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
