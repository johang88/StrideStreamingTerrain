using System.Runtime.InteropServices;

namespace StrideTerrain.Common;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainChunk
{
    public float MinHeight;
    public float MaxHeight;
    public long HeightmapOffset;
    public long NormalMapOffset;

    public readonly void Write(BinaryWriter writer)
    {
        writer.Write(MinHeight);
        writer.Write(MaxHeight);
        writer.Write(HeightmapOffset);
        writer.Write(NormalMapOffset);
    }

    public void Read(BinaryReader reader)
    {
        MinHeight = reader.ReadSingle();
        MaxHeight = reader.ReadSingle();
        HeightmapOffset = reader.ReadInt64();
        NormalMapOffset = reader.ReadInt64();
    }
}
