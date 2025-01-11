// See https://aka.ms/new-console-template for more information
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.TextureConverter;
using StrideTerrain.Common;
using System.CommandLine;
using System.Text.Json;

var inputOption = new Option<FileInfo>(
    name: "--input",
    description: "Heightmap input file, must be square");

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

var treesOptions = new Option<bool>(
    name: "--trees",
    description: "Generate tree location");

var rootCommand = new RootCommand("StrideTerrain Importer");
rootCommand.AddOption(inputOption);
rootCommand.AddOption(outputPathOption);
rootCommand.AddOption(chunkSizeOption);
rootCommand.AddOption(maxHeightOption);
rootCommand.AddOption(maxLodOption);
rootCommand.AddOption(nameOption);
rootCommand.AddOption(treesOptions);

static float ConvertToFloatHeight(float minValue, float maxValue, float value) => MathUtil.InverseLerp(minValue, maxValue, MathUtil.Clamp(value, minValue, maxValue));

rootCommand.SetHandler((input, outputPath, name, chunkSize, maxHeight, maxLod, generateTreeData) =>
{
    using var textureTool = new TextureTool();
    using var heightmap = textureTool.Load(input.FullName, false);

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

    unsafe ushort[] LoadHeightData()
    {
        var data = (ushort*)heightmap.Data;
        var pixelSize = heightmap.Format == PixelFormat.R16G16B16A16_UNorm ? 4 : 1;
        var heights = new ushort[terrainSize * terrainSize];

        for (var y = 0; y < terrainSize; y++)
        {
            for (var x = 0; x < terrainSize; x++)
            {
                heights[y * terrainSize + x] = data[(y * heightmap.Width + x) * pixelSize];
            }
        }

        return heights;
    }

    var heights = LoadHeightData();

    float HeightAt(int x, int y)
    {
        x = Math.Clamp(x, 0, terrainSize - 1);
        y = Math.Clamp(y, 0, terrainSize - 1);
        return ConvertToFloatHeight(0, ushort.MaxValue, heights[y * terrainSize + x]) * maxHeight;
    }

    Vector3 NormalAt(int x, int y)
    {
        var heightL = HeightAt(x - 1, y);
        var heightR = HeightAt(x + 1, y);
        var heightD = HeightAt(x, y - 1);
        var heightU = HeightAt(x, y + 1);

        var normal = new Vector3(heightL - heightR, 2.0f, heightD - heightU);
        normal.Normalize();

        return normal;
    }

    if (generateTreeData)
    {
        var trees = new List<TreeInstance>(terrainSize * terrainSize);
        for (var y = 0; y < terrainSize; y += 32)
        {
            for (var x = 0; x < terrainSize; x += 32)
            {
                var ox = Random.Shared.Next(0, 31);
                var oy = Random.Shared.Next(0, 32);
                var height = HeightAt(x + ox, y + oy);
                if (height >= 85 && height < 200)
                {
                    var normal = NormalAt(x + ox, y + oy);
                    if (Math.Abs(normal.Z) < 0.05f)
                    {
                        trees.Add(new()
                        {
                            X = (x + ox) * 0.45f,
                            Y = height,
                            Z = (y + oy) * 0.45f
                        });
                    }
                }
            }
        }

        var outputPathTreeData = Path.Combine(outputPath, $"{name}_Trees.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            IncludeFields = true
        };
        File.WriteAllText(outputPathTreeData, JsonSerializer.Serialize(trees, options));

        return;
    }

    var actualMaxLod = (int)Math.Log2(terrainSize / chunkSize); // Max lod = single chunk

    maxLod = maxLod < 0 ? actualMaxLod : Math.Min(actualMaxLod, maxLod);

    var chunks = new List<TerrainChunk>();
    var lodChunkOffsets = new List<int>();

    // Write output stream data
    var textureSize = chunkSize + 1;
    WriteStreamingData(outputPath, name, chunkSize, maxLod, heightmap, terrainSize, maxHeight, textureSize, chunks);

    lodChunkOffsets.Reverse();

    // Write terrain data
    var data = new TerrainData
    {
        Header = new()
        {
            Version = TerrainDataHeader.VERSION,
            ChunkSize = chunkSize,
            ChunkTextureSize = textureSize,
            Size = terrainSize,
            MaxHeight = maxHeight,
            HeightmapSize = textureSize * textureSize * sizeof(ushort),
            NormalMapSize = textureSize * textureSize * sizeof(byte) * 4,
            MaxLod = maxLod
        },
        LodChunkOffsets = [.. lodChunkOffsets],
        Chunks = [.. chunks]
    };

    var outputPathTerrainData = Path.Combine(outputPath, $"{name}");

    if (File.Exists(outputPathTerrainData))
    {
        File.Delete(outputPathTerrainData);
    }

    using var outputStream = File.Open(outputPathTerrainData, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Delete);
    using var writer = new BinaryWriter(outputStream);

    data.Write(writer);

    void WriteStreamingData(string outputPath, string name, int chunkSize, int maxLod, TexImage heightmap, int terrainSize, float maxHeight, int textureSize, List<TerrainChunk> chunks)
    {
        var outputPathStreamData = Path.Combine(outputPath, $"{name}_StreamingData");

        if (File.Exists(outputPathStreamData))
        {
            File.Delete(outputPathStreamData);
        }

        using var outputStream = File.Open(outputPathStreamData, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Delete);
        using var writer = new BinaryWriter(outputStream);

        var chunkHeightmap = new ushort[textureSize * textureSize];
        var chunkNormalMap = new byte[textureSize * textureSize * 4];

        for (var lod = maxLod; lod >= 0; lod--)
        {
            lodChunkOffsets.Add(chunks.Count);

            var scale = 1 << lod;
            var chunksPerRowCurrentLod = terrainSize / (scale * chunkSize);

            for (var y = 0; y < chunksPerRowCurrentLod; y++)
            {
                for (var x = 0; x < chunksPerRowCurrentLod; x++)
                {
                    FillChunkHeightmap(x, y, scale, textureSize);
                    FillChunkNormalMap(x, y, scale, textureSize);

                    ushort localMinHeight = ushort.MaxValue;
                    ushort localMaxHeight = ushort.MinValue;

                    for (var i = 0; i < chunkHeightmap.Length; i++)
                    {
                        localMinHeight = Math.Min(localMinHeight, chunkHeightmap[i]);
                        localMaxHeight = Math.Max(localMaxHeight, chunkHeightmap[i]);
                    }

                    var heightmapOffset = writer.BaseStream.Position;
                    foreach (var v in chunkHeightmap)
                    {
                        writer.Write(v);
                    }

                    var normalMapOffset = writer.BaseStream.Position;
                    foreach (var v in chunkNormalMap)
                    {
                        writer.Write(v);
                    }

                    chunks.Add(new()
                    {
                        HeightmapOffset = heightmapOffset,
                        NormalMapOffset = normalMapOffset,
                        MinHeight = ConvertToFloatHeight(0, ushort.MaxValue, localMinHeight) * maxHeight,
                        MaxHeight = ConvertToFloatHeight(0, ushort.MaxValue, localMaxHeight) * maxHeight
                    });
                }
            }
        }

        void FillChunkHeightmap(int cx, int cy, int scale, int textureSize)
        {
            for (var y = 0; y < textureSize; y++)
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var hx = Math.Clamp(cx * chunkSize * scale + x * scale, 0, terrainSize - 1);
                    var hy = Math.Clamp(cy * chunkSize * scale + y * scale, 0, terrainSize - 1);

                    var height = heights[hy * terrainSize + hx];
                    chunkHeightmap[y * textureSize + x] = height;
                }
            }
        }

        void FillChunkNormalMap(int cx, int cy, int scale, int textureSize)
        {
            for (var y = 0; y < textureSize; y++)
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var hx = cx * chunkSize * scale + x * scale;
                    var hy = cy * chunkSize * scale + y * scale;

                    var normal = NormalAt(hx, hy);
                    normal = normal * 0.5f + new Vector3(0.5f, 0.5f, 0.5f);

                    var index = (y * textureSize + x) * 4;
                    chunkNormalMap[index + 0] = (byte)(normal.X * 255);
                    chunkNormalMap[index + 1] = (byte)(normal.Y * 255);
                    chunkNormalMap[index + 2] = (byte)(normal.Z * 255);
                    chunkNormalMap[index + 3] = 255;
                }
            }
        }
    }
}, inputOption, outputPathOption, nameOption, chunkSizeOption, maxHeightOption, maxLodOption, treesOptions);

await rootCommand.InvokeAsync(args);