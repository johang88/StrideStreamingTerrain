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
    public const int ClipmapTiles = 16;  // tiles per axis per mip level
    // Total slots = MipCount * ClipmapTiles^2 = 7 * 256 = 1792
    // floor(16384 / 264) = 62 → fills atlas width; needs ceil(1792/62) = 29 rows → 29×264 = 7656 px tall
    public const int AtlasTilesPerRow = 62;
}
