using System.Runtime.InteropServices;

namespace StrideTerrain.TerrainSystem.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct ChunkData
{
    public int Data0;       // byte: LodLevel, North, South, West
    public int Data1;       // byte: East, ChunkX, ChunkZ, Padding
    public int PackedUv;    // byte: UvX (low), UvY (high)
    public int PackedPositionXZ;

    public byte LodLevel
    {
        readonly get => (byte)(Data0 & 0xFF);
        set => Data0 = (Data0 & ~0xFF) | value;
    }

    public byte North
    {
        readonly get => (byte)((Data0 >> 8) & 0xFF);
        set => Data0 = (Data0 & ~(0xFF << 8)) | (value << 8);
    }

    public byte South
    {
        readonly get => (byte)((Data0 >> 16) & 0xFF);
        set => Data0 = (Data0 & ~(0xFF << 16)) | (value << 16);
    }

    public byte West
    {
        readonly get => (byte)((Data0 >> 24) & 0xFF);
        set => Data0 = (Data0 & ~(0xFF << 24)) | (value << 24);
    }

    public byte East
    {
        readonly get => (byte)(Data1 & 0xFF);
        set => Data1 = (Data1 & ~0xFF) | value;
    }

    public byte ChunkX
    {
        readonly get => (byte)((Data1 >> 8) & 0xFF);
        set => Data1 = (Data1 & ~(0xFF << 8)) | (value << 8);
    }

    public byte ChunkZ
    {
        readonly get => (byte)((Data1 >> 16) & 0xFF);
        set => Data1 = (Data1 & ~(0xFF << 16)) | (value << 16);
    }

    public ushort UvX
    {
        readonly get => (ushort)(PackedUv & 0xFFFF);
        set => PackedUv = (PackedUv & unchecked((int)0xFFFF0000)) | value;
    }

    public ushort UvY
    {
        readonly get => (ushort)((PackedUv >> 16) & 0xFFFF);
        set => PackedUv = (PackedUv & unchecked((int)0x0000FFFF)) | (value << 16);
    }

    public ushort PositionX
    {
        readonly get => (ushort)(PackedPositionXZ & 0xFFFF);
        set => PackedPositionXZ = (PackedPositionXZ & unchecked((int)0xFFFF0000)) | value;
    }

    public ushort PositionZ
    {
        readonly get => (ushort)((PackedPositionXZ >> 16) & 0xFFFF);
        set => PackedPositionXZ = (PackedPositionXZ & unchecked((int)0x0000FFFF)) | (value << 16);
    }

    public override readonly string ToString()
        => $"n:{North}, s:{South}, w:{West}, e:{East}";
};