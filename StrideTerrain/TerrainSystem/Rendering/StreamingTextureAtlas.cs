using Stride.Graphics;
using System;

namespace StrideTerrain.TerrainSystem.Rendering;

public sealed class StreamingTextureAtlas(GraphicsDevice graphicsDevice, PixelFormat pixelFormat, int size, int chunkSize, int chunkTextureSize, bool renderToStaging = false) : IDisposable
{
    public readonly int Size = size;
    public readonly int ChunkSize = chunkSize;
    public readonly int ChunksPerRow = size / chunkSize;
    public readonly int ChunkTextureSize = chunkTextureSize;

    public Texture AtlasTexture = Texture.New2D(graphicsDevice, size, size, pixelFormat);
    public Texture StagingTexutre = Texture.New2D(graphicsDevice, chunkTextureSize, chunkTextureSize, pixelFormat,
        usage: renderToStaging ? GraphicsResourceUsage.Default : GraphicsResourceUsage.Dynamic,
        textureFlags: (renderToStaging ? TextureFlags.RenderTarget : TextureFlags.None) | TextureFlags.ShaderResource);

    public int BytesPerPixel => pixelFormat switch
    {
        PixelFormat.R16_UNorm => 2,
        PixelFormat.R16_UInt => 2,
        PixelFormat.BC5_UNorm => 0,
        PixelFormat.R8G8_UNorm => 0,
        PixelFormat.R8G8B8A8_UNorm => 0,
        PixelFormat.R8G8B8A8_UNorm_SRgb => 0,
        _ => throw new NotSupportedException($"Pixel format {pixelFormat} is not supported")
    };

    public byte[] Data = new byte[size * size * (pixelFormat switch
    {
        PixelFormat.R16_UNorm => 2,
        PixelFormat.R16_UInt => 2,
        PixelFormat.BC5_UNorm => 0,
        PixelFormat.R8G8_UNorm => 0,
        PixelFormat.R8G8B8A8_UNorm => 0,
        PixelFormat.R8G8B8A8_UNorm_SRgb => 0,
        _ => throw new NotSupportedException($"Pixel format {pixelFormat} is not supported")
    })];

    public bool HasLocalCache => Data.Length > 0;

    public (int tx, int ty) GetCoordinates(int textureIndex)
    {
        var tx = textureIndex % ChunksPerRow * ChunkSize;
        var ty = textureIndex / ChunksPerRow * ChunkSize;

        return (tx, ty);
    }

    public void UpdateChunk(GraphicsContext graphicsContext, Span<byte> data, int textureIndex)
    {
        var (tx, ty) = GetCoordinates(textureIndex);

        StagingTexutre.SetData(graphicsContext.CommandList, data, 0, 0);
        graphicsContext.CommandList.CopyRegion(StagingTexutre, 0, null, AtlasTexture, 0, tx, ty);

        if (HasLocalCache)
        {
            CopyToLocalCache(data, textureIndex);
        }
    }

    public void UpdateChunkFromStaging(GraphicsContext graphicsContext, int textureIndex)
    {
        var (tx, ty) = GetCoordinates(textureIndex);
        graphicsContext.CommandList.CopyRegion(StagingTexutre, 0, null, AtlasTexture, 0, tx, ty);
    }

    private void CopyToLocalCache(Span<byte> data, int textureIndex)
    {
        var (tileX, tileY) = GetCoordinates(textureIndex);

        int atlasStride = Size * BytesPerPixel;
        int tileOffset = tileY * atlasStride + tileX * BytesPerPixel;

        for (int row = 0; row < ChunkTextureSize; row++)
        {
            int dstOffset = tileOffset + row * atlasStride;
            int srcOffset = row * ChunkTextureSize * BytesPerPixel;

            var dataToCopy = data.Slice(srcOffset, ChunkTextureSize * BytesPerPixel);
            var destination = Data.AsSpan(dstOffset);
            dataToCopy.CopyTo(destination);
        }
    }

    public void Dispose()
    {
        AtlasTexture.Dispose();
        StagingTexutre?.Dispose();
    }
}