using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;
using System.Collections.Generic;

namespace StrideTerrain.Vegetation;

[DataContract("Impostor Diffuse")]
public class ComputeImpostorDiffuseColor : IComputeColor
{
    [DataMember(10)]
    public IComputeColor? Diffuse { get; set; }

    [DataMember(20)]
    public IComputeColor? Brightness { get; set; }

    public ShaderSource GenerateShaderSource(ShaderGeneratorContext context, MaterialComputeColorKeys baseKeys)
    {
        var diffuseShaderSource = Diffuse?.GenerateShaderSource(context, baseKeys);
        var brightnessShaderSource = Brightness?.GenerateShaderSource(context, baseKeys);

        var shaderSource = new ShaderClassSource("ComoputeColorImpostor");
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(shaderSource);
        if (diffuseShaderSource != null)
            mixin.AddComposition("Diffuse", diffuseShaderSource);

        if (brightnessShaderSource != null)
            mixin.AddComposition("Brightness", brightnessShaderSource);

        return mixin;
    }

    public IEnumerable<IComputeNode> GetChildren(object? context = null)
    {
        if (Diffuse != null)
            yield return Diffuse;
    }

    public bool HasChanged
    {
        get
        {
            if (Diffuse == null || Brightness == null || !Diffuse.HasChanged || !Brightness.HasChanged)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
