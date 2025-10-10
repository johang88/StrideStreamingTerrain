using Stride.Core.IO;
using Stride.Core.Serialization.Contents;
using StrideTerrain.Common;
using System.IO;

namespace StrideTerrain.TerrainSystem;

/// <summary>
/// Abstracts loading / opening of terrain data streams.
/// </summary>
public interface ITerrainDataProvider
{
    void LoadTerrainData(ref TerrainData terrainData);
    (Stream stream, long baseOffset) OpenStreamingData();
}

public class GameTerrainDataProvider(TerrainComponent terrainComponent, ContentManager contentManager) : ITerrainDataProvider
{
    public void LoadTerrainData(ref TerrainData terrainData)
    {
        using var terrainDataStream = contentManager.OpenAsStream(terrainComponent.TerrainData!.Url, StreamFlags.None);
        using var terrainDataReader = new BinaryReader(terrainDataStream);
        terrainData.Read(terrainDataReader);
    }

    public (Stream stream, long baseOffset) OpenStreamingData()
    {
        DatabaseFileProvider fileProvider = contentManager.FileProvider;

        if (!fileProvider.ContentIndexMap.TryGetValue(terrainComponent.TerrainStreamingData!.Url, out var objectId))
        {
            throw new FileNotFoundException("Could not locate terrain streaming data.");
        }

        if (!fileProvider.ObjectDatabase.TryGetObjectLocation(objectId, out var url, out var startPosition, out var end))
        {
            throw new FileNotFoundException("Could not locate terrain streaming data.");
        }

        return (File.OpenRead(url), startPosition);
    }
}

/// <summary>
/// LOL. this is a hack ... and not a good one.
/// </summary>
public class EditorTerrainDataProvider : ITerrainDataProvider
{
    public void LoadTerrainData(ref TerrainData terrainData)
    {
        // TODO ...
        using var terrainDataStream = File.OpenRead($"C:\\Users\\johan\\Documents\\Stride Projects\\StrideTerrain\\StrideTerrain.Sample\\Resources\\Maps\\Island");
        using var terrainDataReader = new BinaryReader(terrainDataStream);
        terrainData.Read(terrainDataReader);
    }

    public (Stream stream, long baseOffset) OpenStreamingData()
    {
        // TODO ...
        return (File.OpenRead($"C:\\Users\\johan\\Documents\\Stride Projects\\StrideTerrain\\StrideTerrain.Sample\\Resources\\Maps\\Island_StreamingData"), 0);
    }
}