// See https://aka.ms/new-console-template for more information
using Stride.TextureConverter;
using System.CommandLine;
using Stride.Graphics;
using Stride.TextureConverter.Requests;
using System.IO;

var inputOption = new Option<DirectoryInfo>(
    name: "--input",
    description: "Input folder containing a valid texture pack.");

var outputOption = new Option<string>(
    name: "--output",
    description: "Output filename base path, multiple files will be cereated.");

var textureSizeOption = new Option<int>(
    name: "--texture-size",
    description: "Desired texture size.");

var rootCommand = new RootCommand("StrideTerrain TexturePacker");
rootCommand.AddOption(inputOption);
rootCommand.AddOption(outputOption);
rootCommand.AddOption(textureSizeOption);

rootCommand.SetHandler((input, outputPath, textureSize) =>
{
    var textures = input.GetDirectories()
        .Select(textureDir => new TextureImportData(
            textureDir.Name,
            int.Parse(textureDir.Name[0..2]),
            textureDir.GetFiles()
        ))
        .OrderBy(x => x.Index)
        .ToList();

    using var textureTool = new TextureTool();

    var valid = true;
    foreach (var textureGroup in textures)
    {
        var names = textureGroup.Files.Select(x => new
        {
            Key = Path.GetFileNameWithoutExtension(x.Name),
            Value = x
        }).ToDictionary(x => x.Key, x => x.Value);

        textureGroup.Diffuse = ValidateAndLoad("d"); // diffuse
        textureGroup.Normal = ValidateAndLoad("n"); // normal
        textureGroup.Roughness = ValidateAndLoad("r"); // roughness

        TexImage? ValidateAndLoad(string part)
        {
            if (!names.TryGetValue(part, out var path))
            {
                valid = false;
                Console.WriteLine($"{textureGroup.Name} is missing '{part}' part");
                return null;
            }
            else
            {
                var isSrgb = part == "d";

                var texture = textureTool.Load(path.FullName, isSrgb);
                textureTool.Decompress(texture, isSrgb);
                if (texture.Width != texture.Height)
                {
                    Console.WriteLine($"Non square texture {path.FullName}");
                    valid = false;
                    return null;
                }

                //if (texture.Format != PixelFormat.R8G8B8A8_UNorm && texture.Format != PixelFormat.R8G8B8A8_UNorm_SRgb)
                //{
                //    Console.WriteLine($"Non valid texture format {path.FullName}");
                //    valid = false;
                //    return null;
                //}

                if (texture.Width != textureSize)
                {
                    Console.WriteLine($"Resizing {textureGroup.Name}_{part}");
                    textureTool.Resize(texture, textureSize, textureSize, Filter.Rescaling.Lanczos3);
                }

                var outputFormat = PixelFormat.BC1_UNorm_SRgb;
                if (part == "n")
                    outputFormat = PixelFormat.BC5_UNorm;
                else if (part == "r")
                    outputFormat = PixelFormat.BC4_UNorm;

                Console.WriteLine($"Generating mip maps for {textureGroup.Name}_{part}");
                textureTool.GenerateMipMaps(texture, Filter.MipMapGeneration.Box);
                Console.WriteLine($"Compressing {textureGroup.Name}_{part}");
                textureTool.Compress(texture, outputFormat, TextureQuality.Best);

                return texture;
            }
        }
    }

    if (!valid)
    {
        Console.WriteLine("PACK NOT VALID!");
        return;
    }

    Console.WriteLine($"Creating diffuse texture array");
    var diffuseArray = textureTool.CreateTextureArray(textures.Select(x => x.Diffuse).ToList());
    Console.WriteLine($"Creating normal texture array");
    var normalArray = textureTool.CreateTextureArray(textures.Select(x => x.Normal).ToList());
    Console.WriteLine($"Creating roughness texture array");
    var roughnessArray = textureTool.CreateTextureArray(textures.Select(x => x.Roughness).ToList());

    textureTool.Save(diffuseArray, outputPath + "_diffuse.dds");
    textureTool.Save(normalArray, outputPath + "_normal.dds");
    textureTool.Save(roughnessArray, outputPath + "_roughness.dds");

}, inputOption, outputOption, textureSizeOption);

await rootCommand.InvokeAsync(args);

internal class TextureImportData
{
    public string Name { get; }
    public int Index { get; }
    public FileInfo[] Files { get; }
    public TexImage? Diffuse { get; set; }
    public TexImage? Normal { get; set; }
    public TexImage? Roughness { get; set; }

    public TextureImportData(string name, int index, FileInfo[] files)
    {
        Name = name;
        Index = index;
        Files = files;
    }
}