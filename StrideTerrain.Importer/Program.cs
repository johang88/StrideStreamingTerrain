// See https://aka.ms/new-console-template for more information
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.TextureConverter;
using StrideTerrain.Common;
using StrideTerrain.Importer;
using System.CommandLine;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

var inputOption = new Option<FileInfo>(
    name: "--input",
    description: "Heightmap input file, must be square");

var controlMapOption = new Option<FileInfo>(
    name: "--control-map",
    description: "Witcher 3 style control map");

var outputPathOption = new Option<string>(
    name: "--output",
    description: "Output path");

var nameOption = new Option<string>(
    name: "--name",
    description: "Name");

var chunkSizeOption = new Option<int>(
    name: "--chunk-size",
    description: "Chunk size");

var maxLodOption = new Option<int>(
    name: "--max-lod",
    description: "Max height",
    getDefaultValue: () => -1);

var maxHeightOption = new Option<float>(
    name: "--max-height",
    description: "Max height");

var unitsPerTexelOption = new Option<float>(
    name: "--units-per-texel",
    description: "Units per texel");

var rootCommand = new RootCommand("StrideTerrain Importer");
rootCommand.AddOption(inputOption);
rootCommand.AddOption(controlMapOption);
rootCommand.AddOption(outputPathOption);
rootCommand.AddOption(chunkSizeOption);
rootCommand.AddOption(unitsPerTexelOption);
rootCommand.AddOption(maxHeightOption);
rootCommand.AddOption(maxLodOption);
rootCommand.AddOption(nameOption);

const bool CompressNormals = true;

static float ConvertToFloatHeight(float minValue, float maxValue, float value) => MathUtil.InverseLerp(minValue, maxValue, MathUtil.Clamp(value, minValue, maxValue));

rootCommand.SetHandler((input, controlMapInput, outputPath, name, chunkSize, maxHeight, unitsPerTexel, maxLod) =>
{
    var start = DateTime.UtcNow;

    using var textureTool = new TextureTool();
    using var heightmap = textureTool.Load(input.FullName, false);
    //using var controlMapData = textureTool.Load(controlMapInput.FullName, false);

    if (heightmap.Width != heightmap.Height)
    {
        Console.WriteLine("Heightmap must be square.");
        return;
    }

    if (heightmap.Format != PixelFormat.R16_UNorm && heightmap.Format != PixelFormat.R16G16B16A16_UNorm)
    {
        Console.WriteLine("Heightmap must be R16_UNorm  .");
        return;
    }

    var terrainSize = heightmap.Width;
    if (!MathUtil.IsPow2(terrainSize))
    {
        if (MathUtil.IsPow2(terrainSize - 1))
        {
            terrainSize -= 1;
        }
        else
        {
            Console.WriteLine("Heightmap must be power of two.");
            return;
        }
    }

    unsafe ushort[] LoadData(TexImage image)
    {
        var data = (ushort*)image.Data;
        var pixelSize = image.Format == PixelFormat.R16G16B16A16_UNorm ? 4 : 1;
        var heights = new ushort[terrainSize * terrainSize];

        for (var y = 0; y < terrainSize; y++)
        {
            for (var x = 0; x < terrainSize; x++)
            {
                heights[y * terrainSize + x] = data[(y * image.Width + x) * pixelSize];
            }
        }

        return heights;
    }

    Console.WriteLine("Loading heights");
    var heights = LoadData(heightmap);

    float HeightAt(int x, int y)
    {
        x = Math.Clamp(x, 0, terrainSize - 1);
        y = Math.Clamp(y, 0, terrainSize - 1);
        return ConvertToFloatHeight(0, ushort.MaxValue, heights[y * terrainSize + x]) * maxHeight;
    }

    Vector3 NormalAt(int x, int y)
    {
        float hL = HeightAt(x - 1, y);  // Left
        float hR = HeightAt(x + 1, y);  // Right
        float hD = HeightAt(x, y - 1);  // Down
        float hU = HeightAt(x, y + 1);  // Up

        var scale = new Vector3(unitsPerTexel * terrainSize, 1.0f, unitsPerTexel * terrainSize);
        var dx = new Vector3(2.0f, hR - hL, 0.0f);
        var dz = new Vector3(0.0f, hU - hD, 2.0f);

        var normal = Vector3.Normalize(Vector3.Cross(dz, dx));

        return normal;
    }

    unsafe byte[] LoadNormals()
    {
        var normals = new byte[terrainSize * terrainSize * 2];
        Parallel.For(0, terrainSize, y =>
        {
            for (var x = 0; x < terrainSize; x++)
            {
                var normal = NormalAt(x, y);
                normal = (normal + 1.0f) * 0.5f;

                var index = (y * terrainSize + x) * 2;
                normals[index + 0] = (byte)(normal.X * 255);
                // normals[index + 1] = (byte)(normal.Y * 255);, Y is be reconustrcuted at runtime.
                normals[index + 1] = (byte)(normal.Z * 255);
            }
        });

        return normals;
    }

    Console.WriteLine("Loading normals");
    var normals = LoadNormals();

    unsafe ushort[] GenerateControlMap()
    {
        var controlMap = new ushort[terrainSize * terrainSize];
        Parallel.For(0, terrainSize, y =>
        {
            for (var x = 0; x < terrainSize; x++)
            {
                var normal = NormalAt(x, y);

                float height = HeightAt(x, y);
                var worldPos = new Vector3(x * unitsPerTexel, height, y * unitsPerTexel);

                controlMap[y * terrainSize + x] = TerrainControlMap.ComputeControlValue(height, normal, worldPos);
            }
        });

        return controlMap;
    }

    Console.WriteLine("Generating control map");
    var controlMap = GenerateControlMap();

    (byte x, byte y) GetNormal(int x, int y)
    {
        x = Math.Clamp(x, 0, terrainSize - 1);
        y = Math.Clamp(y, 0, terrainSize - 1);

        var index = (y * terrainSize + x) * 2;
        return (normals[index + 0], normals[index + 1]);
    }

    var trees = new List<TreeInstance>();
    var genereateTrees = true;
    if (genereateTrees)
    {
        Console.WriteLine("Generating trees");

        // Define bounding radius per tree type (0–5 = pine, 6–11 = poplar)
        float[] treeRadii = { 3f, 3f, 5f, 7f, 7f, 8f, 3f, 3f, 5f, 7f, 7f, 8f };

        // Spatial hash parameters
        float cellSize = 8f; // Roughly the largest radius
        var grid = new Dictionary<(int, int), List<TreeInstance>>();

        int tileStep = 22;
        int maxAttemptsPerTile = 10;

        for (int y = 0; y < terrainSize; y += tileStep)
        {
            for (int x = 0; x < terrainSize; x += tileStep)
            {
                for (int attempt = 0; attempt < maxAttemptsPerTile; attempt++)
                {
                    int px = x + Random.Shared.Next(0, tileStep);
                    int py = y + Random.Shared.Next(0, tileStep);
                    float height = HeightAt(px, py);
                    Vector3 normal = NormalAt(px, py);

                    if (height < 70 || height >= 140 || Math.Abs(normal.Y) < 0.9f)
                        continue;

                    ushort control = controlMap[py * terrainSize + px];
                    int textureIndex = control & 0x1F;
                    if (textureIndex != 0 && textureIndex != 17 && textureIndex != 20 && textureIndex != 27)
                        continue;

                    float density = Noise.LowFreqNoise(new Vector2(px, py) * 0.05f);
                    if (density < 0.35f) continue;

                    float pineWeight = Math.Clamp((height - 70f) / 60f + (1f - normal.Y) * 2f, 0f, 1f);
                    bool usePine = Random.Shared.NextDouble() < pineWeight;

                    int treeType;
                    float r = (float)Random.Shared.NextDouble();
                    if (r < 0.5f)
                        treeType = Random.Shared.Next(0, 2);
                    else if (r < 0.85f)
                        treeType = 2;
                    else
                        treeType = Random.Shared.Next(3, 6);

                    if (!usePine)
                        treeType += 6; // poplar variant

                    float radius = treeRadii[treeType];

                    // --- Spatial hash lookup ---
                    int gx = (int)((px * unitsPerTexel) / cellSize);
                    int gz = (int)((py * unitsPerTexel) / cellSize);
                    bool tooClose = false;

                    for (int yy = -1; yy <= 1 && !tooClose; yy++)
                    {
                        for (int xx = -1; xx <= 1 && !tooClose; xx++)
                        {
                            if (!grid.TryGetValue((gx + xx, gz + yy), out var nearby)) continue;
                            foreach (var t in nearby)
                            {
                                float dx = t.X - (px * unitsPerTexel);
                                float dz = t.Z - (py * unitsPerTexel);
                                float minDist = MathF.Max(radius, treeRadii[t.Type]);
                                if (dx * dx + dz * dz < minDist * minDist)
                                {
                                    tooClose = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (tooClose) continue;

                    var tree = new TreeInstance
                    {
                        X = px * unitsPerTexel,
                        Y = height,
                        Z = py * unitsPerTexel,
                        Type = treeType,
                        Scale = (float)Random.Shared.NextDouble() + 1.0f
                    };
                    trees.Add(tree);

                    // --- Insert into spatial grid ---
                    var key = (gx, gz);
                    if (!grid.TryGetValue(key, out var list))
                    {
                        list = new List<TreeInstance>();
                        grid[key] = list;
                    }
                    list.Add(tree);
                }
            }
        }

        var prefab = TreePrefab.GeneratePrefab(trees);
        var outputPathTreeData = Path.Combine(outputPath, $"Island_Trees_Prefab.sdprefab");
        File.WriteAllText(outputPathTreeData, prefab);
    }

    var actualMaxLod = (int)Math.Log2(terrainSize / chunkSize); // Max lod = single chunk

    maxLod = maxLod < 0 ? actualMaxLod : Math.Min(actualMaxLod, maxLod);

    var chunks = new List<TerrainChunk>();
    var lodChunkOffsets = new List<int>();

    // Write output stream data
    var textureSize = chunkSize + 1;
    var normalMapTextureSize = chunkSize + 4;
    WriteStreamingData(outputPath, name, chunkSize, maxLod, heightmap, terrainSize, maxHeight, textureSize, normalMapTextureSize, chunks);

    lodChunkOffsets.Reverse();

    // Write terrain data
    var data = new TerrainData
    {
        Header = new()
        {
            Version = TerrainDataHeader.VERSION,
            ChunkSize = chunkSize,
            ChunkTextureSize = textureSize,
            NormalMapTextureSize = normalMapTextureSize,
            Size = terrainSize,
            UnitsPerTexel = unitsPerTexel,
            MaxHeight = maxHeight,
            HeightmapSize = textureSize * textureSize * sizeof(ushort),
            ControlMapSize = textureSize * textureSize * sizeof(ushort),
            MaxLod = maxLod,
            CompressedNormalMap = CompressNormals
        },
        LodChunkOffsets = [.. lodChunkOffsets],
        Chunks = [.. chunks]
    };

    var outputPathTerrainData = Path.Combine(outputPath, $"{name}");

    if (File.Exists(outputPathTerrainData))
    {
        File.Delete(outputPathTerrainData);
    }

    Console.WriteLine("Generating mini map");
    var waterHeight = 55.0f;
    var miniMapSize = 1024;
    var miniMapToTerrain = terrainSize / miniMapSize;
    TerrainMiniMap.Generate(miniMapSize,
        1.0f / unitsPerTexel,
        terrainSize,
        (x, y) => HeightAt(x * miniMapToTerrain, y * miniMapToTerrain),
        (x, y) => controlMap[y * miniMapToTerrain * terrainSize + x * miniMapToTerrain],
        trees,
        Path.Combine(outputPath, $"{name}_MiniMap.png"),
        waterHeight);

    Console.WriteLine("Generating biome map");
    var biomeMap = BiomeMap.Generate(
        terrainSize,
        1024,
        1.0f / unitsPerTexel,
        (x, y) => HeightAt(x, y),
        (x, y) => controlMap[y * terrainSize + x],
        waterHeight,
        trees,
        Path.Combine(outputPath, $"{name}_BiomeDebug.png"));

    File.WriteAllBytes(Path.Combine(outputPath, $"{name}_Biomes"), biomeMap);

    using var outputStream = File.Open(outputPathTerrainData, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Delete);
    using var writer = new BinaryWriter(outputStream);

    data.Write(writer);

    Console.WriteLine($"Completed in {(DateTime.UtcNow - start).TotalSeconds:0.00} seconds.");

    unsafe void WriteStreamingData(string outputPath, string name, int chunkSize, int maxLod, TexImage heightmap, int terrainSize, float maxHeight, int textureSize, int normalMapTextureSize, List<TerrainChunk> chunks)
    {
        var outputPathStreamData = Path.Combine(outputPath, $"{name}_StreamingData");

        if (File.Exists(outputPathStreamData))
        {
            File.Delete(outputPathStreamData);
        }

        List<ushort[]> heightMips = [heights];
        List<byte[]> normalMips = [normals];
        List<ushort[]> controlMapMips = [controlMap];

        using var outputStream = File.Open(outputPathStreamData, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Delete);
        using var writer = new BinaryWriter(outputStream);

        var chunkHeightmap = new ushort[textureSize * textureSize];
        var chunkControlMap = new ushort[textureSize * textureSize];
        var chunkNormalMap = new byte[normalMapTextureSize * normalMapTextureSize * 2];

        for (var lod = maxLod; lod >= 0; lod--)
        {
            Console.WriteLine($"Processing lod: {lod}");
            lodChunkOffsets.Add(chunks.Count);

            var scale = 1 << lod;
            var chunksPerRowCurrentLod = terrainSize / (scale * chunkSize);

            for (var y = 0; y < chunksPerRowCurrentLod; y++)
            {
                for (var x = 0; x < chunksPerRowCurrentLod; x++)
                {
                    var (localMinHeight, localMaxHeight) = FillChunkHeightmap(x, y, scale, textureSize);
                    FillChunkNormalMap(x, y, scale, normalMapTextureSize);
                    FillChunkControlMap(x, y, scale, textureSize);

                    var normalMapSize = chunkNormalMap.Length;

                    if (CompressNormals)
                    {
                        fixed (byte* ptr = chunkNormalMap)
                        {
                            // Compress
                            using var normalMap = new TexImage((nint)ptr, chunkNormalMap.Length, normalMapTextureSize, normalMapTextureSize, 1, PixelFormat.R8G8_UNorm, 1, 1, TexImage.TextureDimension.Texture2D);
                            textureTool.Compress(normalMap, PixelFormat.BC5_UNorm);

                            // Copy data back
                            normalMapSize = normalMap.DataSize;
                            Marshal.Copy(normalMap.Data, chunkNormalMap, 0, normalMapSize);
                        }
                    }

                    var heightmapOffset = writer.BaseStream.Position;
                    writer.Write(MemoryMarshal.AsBytes(chunkHeightmap.AsSpan()));

                    var normalMapOffset = writer.BaseStream.Position;
                    writer.Write(MemoryMarshal.AsBytes(chunkNormalMap.AsSpan(0, normalMapSize)));

                    var controlMapOffset = writer.BaseStream.Position;
                    writer.Write(MemoryMarshal.AsBytes(chunkControlMap.AsSpan()));

                    chunks.Add(new()
                    {
                        HeightmapOffset = heightmapOffset,
                        NormalMapOffset = normalMapOffset,
                        ControlMapOffset = controlMapOffset,
                        NormalMapSize = normalMapSize,
                        MinHeight = ConvertToFloatHeight(0, ushort.MaxValue, localMinHeight) * maxHeight,
                        MaxHeight = ConvertToFloatHeight(0, ushort.MaxValue, localMaxHeight) * maxHeight
                    });
                }
            }
        }

        (uint localMinHeight, uint localMaxHeight) FillChunkHeightmap(int cx, int cy, int scale, int textureSize)
        {
            ushort localMinHeight = ushort.MaxValue;
            ushort localMaxHeight = ushort.MinValue;

            Parallel.For(0, textureSize, y =>
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var hx = Math.Clamp(cx * chunkSize * scale + x * scale, 0, terrainSize - 1);
                    var hy = Math.Clamp(cy * chunkSize * scale + y * scale, 0, terrainSize - 1);

                    var height = heights[hy * terrainSize + hx];
                    chunkHeightmap[y * textureSize + x] = height;

                    localMinHeight = Math.Min(localMinHeight, height);
                    localMaxHeight = Math.Max(localMaxHeight, height);
                }
            });

            return (localMinHeight, localMaxHeight);
        }

        void FillChunkControlMap(int cx, int cy, int scale, int textureSize)
        {
            Parallel.For(0, textureSize, y =>
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var hx = Math.Clamp(cx * chunkSize * scale + x * scale, 0, terrainSize - 1);
                    var hy = Math.Clamp(cy * chunkSize * scale + y * scale, 0, terrainSize - 1);

                    var value = controlMap[hy * terrainSize + hx];
                    chunkControlMap[y * textureSize + x] = value;
                }
            });
        }

        void FillChunkNormalMap(int cx, int cy, int scale, int textureSize)
        {
            Parallel.For(0, textureSize, y =>
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var hx = cx * chunkSize * scale + x * scale;
                    var hy = cy * chunkSize * scale + y * scale;

                    var (nx, nz) = GetNormal(hx, hy);

                    var index = (y * textureSize + x) * 2;
                    chunkNormalMap[index + 0] = nx;
                    chunkNormalMap[index + 1] = nz;
                }
            });
        }
    }
}, inputOption, controlMapOption, outputPathOption, nameOption, chunkSizeOption, maxHeightOption, unitsPerTexelOption, maxLodOption);

await rootCommand.InvokeAsync(args);

public class TreeInstance
{
    public float X;
    public float Y;
    public float Z;
    public float Scale;
    public int Type;
}

public struct TreeInstanceOutput(float x, float y, float z, float w)
{
    public float X = x, Y = y, Z = z, W = w;
}

public class TreePrefab
{
    public required string Material { get; set; }
    public required string Model { get; set; }
    public Vector2 Size { get; set; }
    public float LodDistance { get; set; }

    public const string PrefabTemplate = """
!PrefabAsset
Id: 417e9d2b-cdf0-43cb-b809-c9930afc9340
SerializedVersion: {Stride: 3.1.0.1}
Tags: []
Hierarchy:
    RootParts:
#ROOTPARTS
    Parts:
#ENTITIES
""";

    public const string EntityTemplate = """
        -   Entity:
                Id: #ENTITY_ID
                Name: #ENTITY_NAME
                Components:
                    #TRANSFORM_GUID: !TransformComponent
                        Id: #TRANSFORM_ID
                        Position: {X: 0.0, Y: 0.0, Z: 0.0}
                        Rotation: {X: 0.0, Y: 0.0, Z: 0.0, W: 1.0}
                        Scale: {X: 1.0, Y: 1.0, Z: 1.0}
                        Children: {}
                    #VEGETATION_GUID: !StrideTerrain.Vegetation.VegetationComponent,StrideTerrain
                        Id: #VEGETATION_ID
                        ImpostorMaterial: #MATERIAL
                        Model: #MODEL
                        ImpostorSize: {X: #SIZE_X, Y: #SIZE_Y}
                        ImpostorLodDistance: #LOD_DISTANCE
                        InstancesJson: "#JSON"
""";

    public const string RootPartTemplate = """
        - ref!! #ENTITY_ID
""";

    public static string GeneratePrefab(List<TreeInstance> trees)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            IncludeFields = true,
            WriteIndented = false
        };

        var treesByType = trees.GroupBy(x => x.Type).ToDictionary(x => x.Key, v => v.ToList());

        var entityIds = treesByType.Keys.GroupBy(x => x).ToDictionary(x => x.Key, v => StringGuid());

        var entities = treesByType.Keys.Select(x => EntityTemplate
            .Replace("#ENTITY_ID", entityIds[x])
            .Replace("#ENTITY_NAME", $"Trees_{x}")
            .Replace("#TRANSFORM_GUID", StringId())
            .Replace("#TRANSFORM_ID", StringGuid())
            .Replace("#VEGETATION_GUID", StringId())
            .Replace("#VEGETATION_ID", StringGuid())
            .Replace("#MATERIAL", TreeTypes[x].Material)
            .Replace("#MODEL", TreeTypes[x].Model)
            .Replace("#SIZE_X", TreeTypes[x].Size.X.ToString("0.0"))
            .Replace("#SIZE_Y", TreeTypes[x].Size.Y.ToString("0.0"))
            .Replace("#LOD_DISTANCE", TreeTypes[x].LodDistance.ToString("0.0"))
            .Replace("#JSON", JsonSerializer.Serialize(treesByType[x].Select(t => new TreeInstanceOutput(t.X, t.Y, t.Z, t.Scale)), options).Replace("\"", "\\\""))
            );

        var rootParts = treesByType.Keys.Select(x => RootPartTemplate.Replace("#ENTITY_ID", entityIds[x]));

        return PrefabTemplate
            .Replace("#ROOTPARTS", string.Join("\n", rootParts))
            .Replace("#ENTITIES", string.Join("\n", entities));

        static string StringGuid() => Guid.NewGuid().ToString();
        static string StringId() => Guid.NewGuid().ToString().Replace("-", ""); ;
    }

    public static readonly List<TreePrefab> TreeTypes = [
        new()
        {
            Material = "f523c726-90e7-4f37-9a0a-ae36f011e4af:Epic/Environment_Set/Impostors/Fir01",
            Model = "6aab6e2a-3c46-4b6e-98c0-6a7ee713645a:Epic/Environment_Set/Models/Fir_01_Plant",
            Size = new(6.0f, 6.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "ed273be8-5e31-4c2a-892c-d011ea9c5b7c:Epic/Environment_Set/Impostors/Fir02",
            Model = "8192197f-4c40-41ef-88a5-4413ba05bf45:Epic/Environment_Set/Models/Fir_02_Small",
            Size = new(8.0f, 8.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "8db3a336-c56c-420d-90f2-3056d1b21a6b:Epic/Environment_Set/Impostors/Fir03",
            Model = "b3f17aed-66c2-404f-b8f5-55dbd2a8115c:Epic/Environment_Set/Models/Fir_03_Medium",
            Size = new(12.0f, 12.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "8ff71e45-1436-486c-81c0-5931e6ddfcee:Epic/Environment_Set/Impostors/Fir04",
            Model = "8a74a859-426e-4dee-af90-8695bd6fd195:Epic/Environment_Set/Models/Fir_04_Standalone",
            Size = new(20.0f, 20.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "88fc3fd2-f805-440f-9ca4-9e3b9ba55bfe:Epic/Environment_Set/Impostors/Fir06",
            Model = "3fccb271-0f9a-4434-a571-5c1b9efa8a7a:Epic/Environment_Set/Models/Fir_06_Forest",
            Size = new(20.0f, 20.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "b0b76227-e013-485f-a2c6-ce652987e59a:Epic/Environment_Set/Impostors/Fir07",
            Model = "d9ec25fd-65d0-421e-a81a-3d7179d08c86:Epic/Environment_Set/Models/Fir_07_Forest",
            Size = new(20.0f, 20.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "6f2a84f9-3ff6-413e-9848-1b8048ec2b3f:Epic/Environment_Set/Impostors/Poplar04",
            Model = "b3b27e9b-da88-415e-a2a8-b677606162ae:Epic/Environment_Set/Models/Poplar_04_Small",
            Size = new(12.0f, 12.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "5f03d417-bebc-4570-9e22-ea19730a1867:Epic/Environment_Set/Impostors/Poplar05",
            Model = "1a8f6434-f7be-4a19-b299-8eb944e2088f:Epic/Environment_Set/Models/Poplar_05_Small",
            Size = new(10.0f, 10.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "2b46719d-c003-4966-a290-d3ed73057480:Epic/Environment_Set/Impostors/Poplar03",
            Model = "ce1aa4d9-d596-4c4c-ba83-397a4d5896a8:Epic/Environment_Set/Models/Poplar_03_Medium",
            Size = new(16.0f, 16.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "aaf774b2-6c33-4767-bc70-62f8ff9ef3a2:Epic/Environment_Set/Impostors/Poplar06",
            Model = "cfb3e33d-8ed2-4991-aa35-b0fd013c0382:Epic/Environment_Set/Models/Poplar_06_Forest",
            Size = new(28.0f, 28.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "7257da3b-6694-4b7a-9b04-876c7647125b:Epic/Environment_Set/Impostors/Poplar07",
            Model = "677903c9-bd50-47e1-83b2-493b323e2ebe:Epic/Environment_Set/Models/Poplar_07_Forest",
            Size = new(22.0f, 22.0f),
            LodDistance = 64.0f
        },
        new()
        {
            Material = "66090946-2d77-4e1e-ae1c-5ea71cdff1fd:Epic/Environment_Set/Impostors/Poplar01",
            Model = "6854ee8f-4caf-4676-9c13-3a09b00c3d01:Epic/Environment_Set/Models/Poplar_01_Standalone",
            Size = new(18.0f, 18.0f),
            LodDistance = 64.0f
        }];

}