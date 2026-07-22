using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;
using System.Collections.Generic;

namespace StrideTerrain.Vegetation.Materials;

/// <summary>
/// Material node that reads the runtime baked impostor diffuse atlas.
///
/// It takes no texture of its own: the atlas is produced by ImpostorBaker after the material has
/// already been compiled, so VegetationProcessor binds it by parameter key each frame instead.
/// </summary>
[DataContract("Impostor Atlas Diffuse")]
[Display("Impostor Atlas Diffuse")]
public class ComputeImpostorAtlasColor : IComputeColor
{
    public ShaderSource GenerateShaderSource(ShaderGeneratorContext context, MaterialComputeColorKeys baseKeys)
        => new ShaderClassSource("ComputeColorImpostorDiffuse");

    public IEnumerable<IComputeNode> GetChildren(object? context = null) => [];

    public bool HasChanged => false;
}

/// <summary>
/// Material node that reads the runtime baked impostor normal atlas. Plug into
/// MaterialNormalMapFeature's normal map slot.
/// </summary>
[DataContract("Impostor Atlas Normal")]
[Display("Impostor Atlas Normal")]
public class ComputeImpostorAtlasNormal : IComputeColor
{
    public ShaderSource GenerateShaderSource(ShaderGeneratorContext context, MaterialComputeColorKeys baseKeys)
        => new ShaderClassSource("ComputeColorImpostorNormal");

    public IEnumerable<IComputeNode> GetChildren(object? context = null) => [];

    public bool HasChanged => false;
}
