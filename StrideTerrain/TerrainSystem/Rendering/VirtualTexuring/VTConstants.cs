namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public static class VTConstants
{
    public const int TileSize = 256;
    public const int TileBorder = 4;
    public const int TileSizePadded = TileSize + TileBorder * 2;  // 264
    public const float BaseTileWorld = 0.5f;  // mip 0 tile covers 0.5 m
    public const int MipCount = 9;
    public const int MipBias = -4;

    // Clipmap constants
    public const int ClipmapTiles = 8;  // tiles per axis per mip level
    // Total slots = MipCount * ClipmapTiles^2 = 9 * 64 = 576
    // ceil(sqrt(576)) = 24 → 24×264 = 6336 px (fits in 8192 atlas)
    public const int AtlasTilesPerRow = 24;
}
