using Stride.Core.IO;
using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;
using Stride.Core.Storage;
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
        var streamingDataUrl = terrainComponent.TerrainStreamingData!.Url;

        if (!TryGetObjectId(fileProvider, streamingDataUrl, out var objectId))
        {
            throw new FileNotFoundException($"Could not locate terrain streaming data '{streamingDataUrl}' in the content index.");
        }

        if (!fileProvider.ObjectDatabase.TryGetObjectLocation(objectId, out var url, out var startPosition, out var end))
        {
            throw new FileNotFoundException($"Could not locate the backing file for terrain streaming data '{streamingDataUrl}' ({objectId}).");
        }

        return (File.OpenRead(url), startPosition);
    }

    /// <summary>
    /// Content index lookup with bare-to-canonical alias fallback, mirroring the private
    /// <c>DatabaseFileProvider.TryGetObjectId</c>. <see cref="UrlReference.Url"/> holds the bare authored
    /// url (Maps/Island_StreamingData) while the shipped index is keyed by canonical, package qualified
    /// urls (/StrideTerrain.Sample/Maps/Island_StreamingData). ContentManager resolves this for us, but
    /// ContentIndexMap is the raw table and does not.
    /// </summary>
    private static bool TryGetObjectId(DatabaseFileProvider fileProvider, string url, out ObjectId objectId)
    {
        if (fileProvider.ContentIndexMap.TryGetValue(url, out objectId))
            return true;

        var aliases = fileProvider.ObjectDatabase.ContentAliases;
        return aliases.TryGetValue(url, out var canonical)
            && fileProvider.ContentIndexMap.TryGetValue(canonical, out objectId);
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