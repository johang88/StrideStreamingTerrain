using System;
using System.Runtime.InteropServices;

namespace StrideTerrain.Common;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainDataHeader
{
    public const int VERSION = 0x11;

    public int Version;
    public int ChunkSize;
    public int ChunkTextureSize;
    public int Size;
    public float UnitsPerTexel;
    public float MaxHeight;
    public int HeightmapSize;
    public int NormalMapSize;
    public int ControlMapSize;
    public int MaxLod;
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
        var buffer = new byte[Marshal.SizeOf<TerrainDataHeader>()];
        MemoryMarshal.Write(buffer, Header);
        writer.Write(buffer);

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
        var header = reader.ReadBytes(Marshal.SizeOf<TerrainDataHeader>());
        Header = MemoryMarshal.Read<TerrainDataHeader>(header);

        if (Header.Version != TerrainDataHeader.VERSION)
            throw new InvalidDataException($"Invalid version, expected {TerrainDataHeader.VERSION}, got {Header.Version}");

        LodChunkOffsets = new int[Header.MaxLod + 1];
        for (var i = 0; i < LodChunkOffsets.Length; i++)
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
