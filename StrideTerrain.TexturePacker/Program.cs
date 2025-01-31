// See https://aka.ms/new-console-template for more information
using Stride.TextureConverter;
using System.CommandLine;
using Stride.Graphics;
using Stride.TextureConverter.Requests;
using CsvHelper.Configuration;
using System.Globalization;
using CsvHelper;
using StrideTerrain.TexturePacker;
using System.Collections.Concurrent;

var inputOption = new Option<DirectoryInfo>(
    name: "--input",
    description: "Input folder containing a valid texture pack.");

var textureSizeOption = new Option<int>(
    name: "--texture-size",
    description: "Desired texture size.");

var rootCommand = new RootCommand("StrideTerrain TexturePacker");
rootCommand.AddOption(inputOption);
rootCommand.AddOption(textureSizeOption);

rootCommand.SetHandler((input, textureSize) =>
{
    var start = DateTime.UtcNow;
    var outputPath = input.FullName.TrimEnd('\\');

    var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
    {
        NewLine = Environment.NewLine,
    };

    using var reader = new StreamReader(Path.Combine(input.FullName, "TexturePack.csv"));
    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
    var materials = csv.GetRecords<Material>().ToList();

    using var textureTool = new TextureTool();

    var diffuseTextures = new ConcurrentBag<(int Index, TexImage Texture)>();
    var normalTextures = new ConcurrentBag<(int Index, TexImage Texture)>();
    var roughnessTextures = new ConcurrentBag<(int Index, TexImage Texture)>();
    var valid = true;
    Parallel.For(0, materials.Count, i =>
    {
        var material = materials[i];
        diffuseTextures.Add((i, ValidateAndLoad(Path.Combine(input.FullName, material.Diffuse), TextureType.Diffuse)));
        normalTextures.Add((i, ValidateAndLoad(Path.Combine(input.FullName, material.Normal), TextureType.Normal)));
        roughnessTextures.Add((i, ValidateAndLoad(Path.Combine(input.FullName, material.Roughness), TextureType.Roughness)));

        TexImage ValidateAndLoad(string path, TextureType textureType)
        {
            var texture = textureTool.Load(path, textureType == TextureType.Diffuse);
            textureTool.Decompress(texture, textureType == TextureType.Diffuse);
            if (texture.Width != texture.Height)
            {
                Console.WriteLine($"Non square texture {path}");
                valid = false;
                return texture;
            }

            if (texture.Width != textureSize)
            {
                Console.WriteLine($"Resizing {path}");
                textureTool.Resize(texture, textureSize, textureSize, Filter.Rescaling.Lanczos3);
            }

            var outputFormat = PixelFormat.BC1_UNorm_SRgb;
            if (textureType == TextureType.Normal)
                outputFormat = PixelFormat.BC5_UNorm;
            else if (textureType == TextureType.Roughness)
                outputFormat = PixelFormat.BC4_UNorm;

            Console.WriteLine($"Generating mip maps for {path}");
            textureTool.GenerateMipMaps(texture, Filter.MipMapGeneration.Box);
            Console.WriteLine($"Compressing {path}");
            textureTool.Compress(texture, outputFormat, TextureQuality.Best);

            return texture;
        }
    });

    if (!valid)
    {
        Console.WriteLine("PACK NOT VALID!");
        return;
    }

    List<TexImage> Resolve(IEnumerable<(int Index, TexImage Texture)> textures)
        => textures.OrderBy(x => x.Index).Select(x => x.Texture).ToList();

    Console.WriteLine($"Creating diffuse texture array");
    var diffuseArray = textureTool.CreateTextureArray(Resolve(diffuseTextures));
    Console.WriteLine($"Creating normal texture array");
    var normalArray = textureTool.CreateTextureArray(Resolve(normalTextures));
    Console.WriteLine($"Creating roughness texture array");
    var roughnessArray = textureTool.CreateTextureArray(Resolve(roughnessTextures));

    textureTool.Save(diffuseArray, outputPath + "_diffuse.dds");
    textureTool.Save(normalArray, outputPath + "_normal.dds");
    textureTool.Save(roughnessArray, outputPath + "_roughness.dds");

    Console.WriteLine($"Completed in {(DateTime.UtcNow - start).TotalSeconds:0.00} seconds.");
}, inputOption, textureSizeOption);

await rootCommand.InvokeAsync(args);
