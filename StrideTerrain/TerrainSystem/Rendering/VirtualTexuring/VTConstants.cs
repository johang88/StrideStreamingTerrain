namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public static class VTConstants
{
    public const int TileSize = 256;
    public const int TileBorder = 4;
    public const int TileSizePadded = TileSize + TileBorder * 2;
    public const float BaseTileWorld = 0.5f; // mip 0 tile covers 0.5m
    public const int MipCount = 9;
    public const int MipBias = -4;
}
