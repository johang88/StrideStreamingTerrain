using System;
using System.Text.Json;

namespace StrideTerrain.Editor;

public class EditorContext
{
    private static JsonSerializerOptions _options = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true,
        IgnoreReadOnlyFields = true,
        IgnoreReadOnlyProperties = true,
        WriteIndented = true
    };

    public TerrainSettings Terrain { get; private set; } = new();

    public void Reset()
    {
        Terrain = new TerrainSettings();
    }

    public void Load(string json)
    {
        Terrain = JsonSerializer.Deserialize<TerrainSettings>(json, _options) ?? throw new Exception("Invalid terrain json");
    }

    public string Save()
    {
        return JsonSerializer.Serialize(Terrain, _options);
    }
}
