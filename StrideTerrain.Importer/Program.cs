// See https://aka.ms/new-console-template for more information
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.TextureConverter;
using StrideTerrain.Common;
using System.CommandLine;
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

var treesOptions = new Option<bool>(
    name: "--trees",
    description: "Generate tree location");

var rootCommand = new RootCommand("StrideTerrain Importer");
rootCommand.AddOption(inputOption);
rootCommand.AddOption(controlMapOption);
rootCommand.AddOption(outputPathOption);
rootCommand.AddOption(chunkSizeOption);
rootCommand.AddOption(unitsPerTexelOption);
rootCommand.AddOption(maxHeightOption);
rootCommand.AddOption(maxLodOption);
rootCommand.AddOption(nameOption);
rootCommand.AddOption(treesOptions);

const bool CompressNormals = true;

static float ConvertToFloatHeight(float minValue, float maxValue, float value) => MathUtil.InverseLerp(minValue, maxValue, MathUtil.Clamp(value, minValue, maxValue));

rootCommand.SetHandler((input, controlMapInput, outputPath, name, chunkSize, maxHeight, unitsPerTexel, maxLod) =>
{
    var start = DateTime.UtcNow;

    using var textureTool = new TextureTool();
    using var heightmap = textureTool.Load(input.FullName, false);
    using var controlMapData = textureTool.Load(controlMapInput.FullName, false);

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
    Console.WriteLine("Loading control map");
    var controlMap = LoadData(controlMapData);

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

    (byte x, byte y) GetNormal(int x, int y)
    {
        x = Math.Clamp(x, 0, terrainSize - 1);
        y = Math.Clamp(y, 0, terrainSize - 1);

        var index = (y * terrainSize + x) * 2;
        return (normals[index + 0], normals[index + 1]);
    }

    bool genereateTrees = true;
    if  (genereateTrees)
    {
        var trees = new List<TreeInstance>(terrainSize * terrainSize);
        for (var y = 0; y < terrainSize; y += 24)
        {
            for (var x = 0; x < terrainSize; x += 24)
            {
                var ox = Random.Shared.Next(1, 23);
                var oy = Random.Shared.Next(1, 23);
                var height = HeightAt(x + ox, y + oy);
                if (height >= 90 && height < 200)
                {
                    var normal = NormalAt(x + ox, y + oy);
                    if (Math.Abs(normal.Y) > 0.9f)
                    {
                        trees.Add(new()
                        {
                            X = (x + ox) * unitsPerTexel,
                            Y = height,
                            Z = (y + oy) * unitsPerTexel
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
                            using var normalMap = new TexImage((nint)ptr, chunkNormalMap.Length, normalMapTextureSize, normalMapTextureSize, 1, PixelFormat.R8G8_UNorm, 1, 1, TexImage.TextureDimension.Texture3D);
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

class TreeInstance
{
    public float X;
    public float Y;
    public float Z;
}