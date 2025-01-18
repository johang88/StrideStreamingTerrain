using System.Runtime.InteropServices;

namespace StrideTerrain.Common;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainDataHeader
{
    public const int VERSION = 0x5;

    public int Version;
    public int ChunkSize;
    public int ChunkTextureSize;
    public int Size;
    public float MaxHeight;
    public int HeightmapSize;
    public int NormalMapSize;
    public int MaxLod;

    public readonly void Write(BinaryWriter writer)
    {
        writer.Write(Version);
        writer.Write(ChunkSize);
        writer.Write(ChunkTextureSize);
        writer.Write(Size);
        writer.Write(MaxHeight);
        writer.Write(HeightmapSize);
        writer.Write(NormalMapSize);
        writer.Write(MaxLod);
    }

    public void Read(BinaryReader reader)
    {
        Version = reader.ReadInt32();

        if (Version != VERSION)
            throw new InvalidDataException($"Invalid version, expected {VERSION}, got {Version}");

        ChunkSize = reader.ReadInt32();
        ChunkTextureSize = reader.ReadInt32();
        Size = reader.ReadInt32();
        MaxHeight = reader.ReadSingle();
        HeightmapSize = reader.ReadInt32();
        NormalMapSize = reader.ReadInt32();
        MaxLod = reader.ReadInt32();
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainData
{
    public TerrainDataHeader Header;
    public int[] LodChunkOffsets;
    public TerrainChunk[] Chunks;

    public readonly int GetNumberOfChunksPerRow(int lod)
        => Header.Size / ((1 << lod) * Header.ChunkSize);

    public readonly int GetChunkIndex(int lod, int chunk)
        => LodChunkOffsets[lod] + chunk;

    public readonly int GetChunkIndex(int lod, int x, int y, int chunksPerRow)
        => LodChunkOffsets[lod] + (y * chunksPerRow + x);

    public readonly void Write(BinaryWriter writer)
    {
        Header.Write(writer);
        foreach (var offset in LodChunkOffsets)
        {
            writer.Write(offset);
        }
        writer.Write(Chunks.Length);
        foreach (var chunk in Chunks)
        {
            chunk.Write(writer);
        }
    }

    public void Read(BinaryReader reader)
    {
        Header.Read(reader);
        LodChunkOffsets = new int[Header.MaxLod + 1];
        for (var i =0; i < LodChunkOffsets.Length; i++)
        {
            LodChunkOffsets[i] = reader.ReadInt32();
        }
        var chunkCount = reader.ReadInt32();
        Chunks = new TerrainChunk[chunkCount];
        for (var i = 0; i < chunkCount; i++)
        {
            Chunks[i].Read(reader);
        }
    }
}
