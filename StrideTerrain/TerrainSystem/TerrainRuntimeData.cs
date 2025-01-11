using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Common;
using System.Collections.Generic;
using System.IO;

namespace StrideTerrain.TerrainSystem;

public class TerrainRuntimeData
{
    public const int RuntimeTextureSize = 2048;
    public const float InvRuntimeTextureSize = 1.0f / RuntimeTextureSize;

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

    public RenderModel? RenderModel;

    public int InstanceCount;

    public Vector3 CameraPosition;

    public BoundingFrustum CameraFrustum;

    public ChunkInstanceData[] ChunkInstanceData = [];

    public int[] SectorToChunkInstanceMap = [];

    public Buffer? ChunkInstanceDataBuffer;

    public Buffer? SectorToChunkInstanceMapBuffer;

    public string? TerrainDataUrl;

    public TerrainData TerrainData;

    // Streaming data 
    public string? TerrainStreamDataUrl;

    public long BaseOffset;

    public Stream? TerrainStream;

    public Texture? HeightmapStagingTexture;

    public Texture? HeightmapTexture;

    public Texture? NormalMapStagingTexture;

    public Texture? NormalMapTexture;

    public int NextFreeIndex = 0;

    public int MaxResidentChunks = 0;

    public int[] ResidentChunks = [];

    public int ResidentChunksCount = 0;

    public HashSet<int> ActiveChunks = [];

    public Stack<int> PendingChunks = [];

    public int[] ChunkToTextureIndex = [];

    public Dictionary<int, Entity> PhysicsEntities = [];

    public Queue<Entity> PhysicsEntityPool = [];
}
