﻿using Stride.Graphics;
using System;

namespace StrideTerrain.TerrainSystem.Rendering;
public sealed class StreamingTextureAtlas(GraphicsDevice graphicsDevice, PixelFormat pixelFormat, int size, int chunkSize, int chunkTextureSize) : IDisposable
{
    public readonly int Size = size;
    public readonly int ChunkSize = chunkSize;
    public readonly int ChunksPerRow = size / chunkSize;

    public Texture AtlasTexture = Texture.New2D(graphicsDevice, size, size, pixelFormat);
    public Texture StagingTexture = Texture.New2D(graphicsDevice, chunkTextureSize, chunkTextureSize, pixelFormat, usage: GraphicsResourceUsage.Default);

    public (int tx, int ty) GetCoordinates(int textureIndex)
    {
        var tx = textureIndex % ChunksPerRow * ChunkSize;
        var ty = textureIndex / ChunksPerRow * ChunkSize;

        return (tx, ty);
    }

    public void UpdateChunk(GraphicsContext graphicsContext, Span<byte> data, int textureIndex)
    {
        var (tx, ty) = GetCoordinates(textureIndex);

        StagingTexture.SetData(graphicsContext.CommandList, data, 0, 0);
        graphicsContext.CommandList.CopyRegion(StagingTexture, 0, null, AtlasTexture, 0, tx, ty);
    }

    public void Dispose()
    {
        AtlasTexture.Dispose();
        StagingTexture.Dispose();
    }
}
