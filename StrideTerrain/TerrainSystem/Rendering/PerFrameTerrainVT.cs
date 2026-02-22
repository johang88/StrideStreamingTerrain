using Stride.Core.Mathematics;
using System.Runtime.InteropServices;

namespace StrideTerrain.TerrainSystem.Rendering;

/// <summary>
/// Must match CBuffer in TerrainData.sdsl
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct PerFrameTerrainVT
{
    public float VTMipBias;
    public float VTMaxAniso;
    public float VTResolution;
    public float Padding0;
    public Vector4 ClipmapOriginsPacked0;
    public Vector4 ClipmapOriginsPacked1;
    public Vector4 ClipmapOriginsPacked2;
    public Vector4 ClipmapOriginsPacked3;
    public Vector4 ClipmapOriginsPacked4;
}
