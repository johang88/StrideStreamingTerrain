﻿using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Common;
using StrideTerrain.TerrainSystem.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.TerrainSystem;

[DataContract]
public class TerrainRuntimeData
{
    public const int RuntimeTextureSize = 2048;
    public const float InvRuntimeTextureSize = 1.0f / RuntimeTextureSize;
    public const int ShadowMapSize = 2048;

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

    public Texture? HeightmapStagingTexture;

    public Texture? HeightmapTexture;

    public Texture? NormalMapStagingTexture;

    public Texture? NormalMapTexture;

    public Texture? ShadowMap;

    public int NextFreeIndex = 0;

    public int MaxResidentChunks = 0;

    public int[] ResidentChunks = [];

    public int ResidentChunksCount = 0;

    public HashSet<int> ActiveChunks = [];

    public Stack<int> PendingChunks = [];

    public int[] ChunkToTextureIndex = [];

    public Dictionary<int, Entity> PhysicsEntities = [];

    public Queue<Entity> PhysicsEntityPool = [];

    // Can be used to check if streaming data has updated.
    public int LastStreamingUpdate; 

    public void ReadChunk(ChunkType chunkType, int chunkIndex, Span<byte> buffer)
    {
        if (TerrainStream == null) throw new InvalidOperationException("Stream not available.");

        var offset = chunkType == ChunkType.Heightmap ? TerrainData.Chunks[chunkIndex].HeightmapOffset : TerrainData.Chunks[chunkIndex].NormalMapOffset;

        TerrainStream.Seek(BaseOffset + offset, SeekOrigin.Begin);
        TerrainStream.ReadAtLeast(buffer, TerrainData.Header.HeightmapSize);
    }

    public bool RequestChunk(int chunkIndex)
    {
        ActiveChunks.Add(chunkIndex);

        if (ChunkToTextureIndex[chunkIndex] == -1)
        {
            PendingChunks.Push(chunkIndex);
            return false;
        }

        return true;
    }
}

public enum ChunkType
{
    Heightmap,
    NormalMap
}
