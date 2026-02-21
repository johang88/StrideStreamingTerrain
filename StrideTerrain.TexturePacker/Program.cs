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

    var diffuseRoughnessTextures = new ConcurrentBag<(int Index, TexImage Texture)>();
    var normalTextures = new ConcurrentBag<(int Index, TexImage Texture)>();
    var valid = true;

    Parallel.For(0, materials.Count, i =>
    {
        var material = materials[i];

        // Load and process diffuse + roughness together
        diffuseRoughnessTextures.Add((i, LoadAndMergeDiffuseRoughness(
            Path.Combine(input.FullName, material.Diffuse),
            Path.Combine(input.FullName, material.Roughness))));

        // Normal maps processed separately
        normalTextures.Add((i, ValidateAndLoad(Path.Combine(input.FullName, material.Normal), TextureType.Normal)));

        TexImage LoadAndMergeDiffuseRoughness(string diffusePath, string roughnessPath)
        {
            // Load both textures (sRGB for diffuse, linear for roughness)
            var diffuse = textureTool.Load(diffusePath, isSRgb: true);
            var roughness = textureTool.Load(roughnessPath, isSRgb: false);

            // Decompress to raw pixel data
            textureTool.Decompress(diffuse, isSRgb: true);
            textureTool.Decompress(roughness, isSRgb: false);

            // Validate square textures
            if (diffuse.Width != diffuse.Height)
            {
                Console.WriteLine($"Non square texture {diffusePath}");
                valid = false;
                return diffuse;
            }
            if (roughness.Width != roughness.Height)
            {
                Console.WriteLine($"Non square texture {roughnessPath}");
                valid = false;
                return diffuse;
            }

            // Resize if needed
            if (diffuse.Width != textureSize)
            {
                Console.WriteLine($"Resizing {diffusePath}");
                textureTool.Resize(diffuse, textureSize, textureSize, Filter.Rescaling.Lanczos3);
            }
            if (roughness.Width != textureSize)
            {
                Console.WriteLine($"Resizing {roughnessPath}");
                textureTool.Resize(roughness, textureSize, textureSize, Filter.Rescaling.Lanczos3);
            }

            // Merge: copy roughness R channel into diffuse A channel
            Console.WriteLine($"Merging {diffusePath} + {roughnessPath}");
            MergeRoughnessIntoAlpha(diffuse, roughness);

            // Generate mip maps and compress
            Console.WriteLine($"Generating mip maps for {diffusePath}");
            textureTool.GenerateMipMaps(diffuse, Filter.MipMapGeneration.Box);

            Console.WriteLine($"Compressing {diffusePath} (with roughness in alpha)");
            textureTool.Compress(diffuse, PixelFormat.BC3_UNorm_SRgb, TextureQuality.Best);

            roughness.Dispose();
            return diffuse;
        }

        void MergeRoughnessIntoAlpha(TexImage diffuse, TexImage roughness)
        {
            int pixelCount = diffuse.Width * diffuse.Height;

            // Determine bytes per pixel from format
            int diffuseBpp = GetBytesPerPixel(diffuse.Format);
            int roughnessBpp = GetBytesPerPixel(roughness.Format);

            Console.WriteLine($"  Diffuse format: {diffuse.Format} ({diffuseBpp} bpp)");
            Console.WriteLine($"  Roughness format: {roughness.Format} ({roughnessBpp} bpp)");

            unsafe
            {
                byte* diffusePtr = (byte*)diffuse.Data.ToPointer();
                byte* roughnessPtr = (byte*)roughness.Data.ToPointer();

                for (int p = 0; p < pixelCount; p++)
                {
                    // Diffuse: write to alpha channel (4th byte in RGBA)
                    // Roughness: read from R channel (1st byte)
                    diffusePtr[p * diffuseBpp + 3] = roughnessPtr[p * roughnessBpp];
                }
            }
        }

        int GetBytesPerPixel(PixelFormat format)
        {
            return format switch
            {
                PixelFormat.R8G8B8A8_UNorm => 4,
                PixelFormat.R8G8B8A8_UNorm_SRgb => 4,
                PixelFormat.B8G8R8A8_UNorm => 4,
                PixelFormat.B8G8R8A8_UNorm_SRgb => 4,
                PixelFormat.R8_UNorm => 1,
                PixelFormat.R32G32B32A32_Float => 16,
                PixelFormat.R32G32B32_Float => 12,
                _ => throw new NotSupportedException($"Unknown format: {format}")
            };
        }

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

            // Normal maps use BC5 (two-channel)
            var outputFormat = PixelFormat.BC5_UNorm;

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

    Console.WriteLine($"Creating diffuse+roughness texture array");
    var diffuseRoughnessArray = textureTool.CreateTextureArray(Resolve(diffuseRoughnessTextures));
    Console.WriteLine($"Creating normal texture array");
    var normalArray = textureTool.CreateTextureArray(Resolve(normalTextures));

    textureTool.Save(diffuseRoughnessArray, outputPath + "_diffuse.dds");
    textureTool.Save(normalArray, outputPath + "_normal.dds");

    Console.WriteLine($"Completed in {(DateTime.UtcNow - start).TotalSeconds:0.00} seconds.");
}, inputOption, textureSizeOption);

await rootCommand.InvokeAsync(args);