using Stride.Core.Mathematics;
using System.Runtime.InteropServices;

namespace StrideTerrain.TerrainSystem.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ChunkData
{
    public byte LodLevel;
    public byte North;
    public byte South;
    public byte West;

    public byte East;
    public byte ChunkX;
    public byte ChunkZ;
    public byte Padding0;

    public float Scale;
    public Vector3 Position;

    public int UvX;
    public int UvY;

    public BoundingBoxExt Bounds;

    public override readonly string ToString()
        => $"n:{North}, s:{South}, w:{West}, e:{East}";
};