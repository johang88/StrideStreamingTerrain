using Stride.Core.Mathematics;
using Stride.Graphics;
using System;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public class PhysicalAtlas(GraphicsDevice device) : IDisposable
{
    public Texture DiffuseRoughnessAtlas { get; private set; } = Texture.New2D(device, 16384, 16384,
            PixelFormat.BC3_UNorm_SRgb, TextureFlags.ShaderResource);
    public Texture NormalAtlas { get; private set; } = Texture.New2D(device, 16384, 16384,
            PixelFormat.BC5_UNorm, TextureFlags.ShaderResource);

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
        DiffuseRoughnessAtlas?.Dispose();
        NormalAtlas?.Dispose();
    }
}
