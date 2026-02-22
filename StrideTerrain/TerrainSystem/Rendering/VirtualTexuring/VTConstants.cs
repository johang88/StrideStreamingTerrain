namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public static class VTConstants
{
    public const int TileSize = 256;
    public const int TileBorder = 4;
    public const int TileSizePadded = TileSize + TileBorder * 2;  // 264
    public const float BaseTileWorld = 0.5f;  // mip 0 tile covers 0.5 m
    public const int MipCount = 7;
    public const float MipBias = -1f;

    // Clipmap constants
    public const int ClipmapTiles = 22;  // tiles per axis per mip level
    // Total slots = MipCount * ClipmapTiles^2 = 7 * 484 = 3388
    // floor(16384 / 264) = 62 → fills atlas width; needs ceil(3388/62) = 55 rows → 55×264 = 14520 px tall (88% of 16384)
    public const int AtlasTilesPerRow = 62;
}
