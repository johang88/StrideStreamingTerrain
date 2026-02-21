using Stride.Core.Mathematics;
using Stride.Graphics;
using System;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public class PhysicalAtlas : IDisposable
{
    // Atlas textures
    public Texture DiffuseAtlas { get; private set; }
    public Texture RoughnessAtlas { get; private set; }
    public Texture NormalAtlas { get; private set; }

    public PhysicalAtlas(GraphicsDevice device)
    {
        DiffuseAtlas = Texture.New2D(device, 8192, 8192,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget);

        NormalAtlas = Texture.New2D(device, 8192, 8192,
            PixelFormat.R16G16_Float,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget);

        RoughnessAtlas = Texture.New2D(device, 8192, 8192,
            PixelFormat.R8_UNorm,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget);
    }

    /// <summary>
    /// Get the atlas pixel region for a slot (includes border).
    /// </summary>
    public static Rectangle GetSlotRegion(int slotIdx)
    {
        int tx = slotIdx % VTConstants.AtlasTilesPerRow;
        int ty = slotIdx / VTConstants.AtlasTilesPerRow;
        return new Rectangle(
            tx * VTConstants.TileSizePadded,
            ty * VTConstants.TileSizePadded,
            VTConstants.TileSizePadded,
            VTConstants.TileSizePadded);
    }

    /// <summary>
    /// Get the deterministic physical slot index for a tile via toroidal addressing.
    /// </summary>
    public static int GetToroidalSlot(int tileX, int tileY, int mipLevel)
    {
        int clipX = ((tileX % VTConstants.ClipmapTiles) + VTConstants.ClipmapTiles) % VTConstants.ClipmapTiles;
        int clipY = ((tileY % VTConstants.ClipmapTiles) + VTConstants.ClipmapTiles) % VTConstants.ClipmapTiles;
        return mipLevel * VTConstants.ClipmapTiles * VTConstants.ClipmapTiles
               + clipY * VTConstants.ClipmapTiles + clipX;
    }

    public void Dispose()
    {
        DiffuseAtlas?.Dispose();
        NormalAtlas?.Dispose();
        RoughnessAtlas?.Dispose();
    }
}
