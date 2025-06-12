using System.Runtime.InteropServices;

namespace StrideTerrain.TerrainSystem.Rendering;

/// <summary>
/// Must match CBuffer in TerrainData.sdsl
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct PerFrameTerrain
{
    public uint ChunkSize;
    public float InvTerrainTextureSize;
    public float TerrainTextureSize;
    public float InvTerrainSize;
    public float InvShadowMapSize;
    public float MaxHeight;
    public float InvMaxHeight;
    public uint ChunksPerRow;
    public float InvUnitsPerTexel;
    public float UnitsPerTexel;
}
