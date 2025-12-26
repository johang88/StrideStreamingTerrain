using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StrideTerrain.Editor;

public class TerrainSettings
{
    public bool EnableShadows = true;
    public System.Numerics.Vector2 SunAngles = new(-160, -90);

    public int ResolutionIndex = 2;
    public static int[] Resolutions = [256, 512, 1024, 2048, 4096, 8192, 16384];
    public static string[] ResolutionStrings = [.. Resolutions.Select(x => x.ToString())];

    public int Resolution => Resolutions[ResolutionIndex];
    public float Size = 512;

    public float MaxHeight = 200;

    [JsonIgnore]
    public float UnitsPerTexel => Size / Resolution;

    [JsonIgnore]
    public bool IsDirty = true;

    public List<TerrainLayer> Layers = [];
}
