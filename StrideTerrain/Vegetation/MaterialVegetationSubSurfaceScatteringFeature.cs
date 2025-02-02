using Stride.Rendering.Materials.ComputeColors;
using Stride.Rendering.Materials;
using Stride.Rendering;
using Stride.Shaders;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Graphics;
using Stride.Core.Mathematics;

namespace StrideTerrain.Vegetation;

public static class SubSurfaceScatteringKeys
{
    public static readonly ObjectParameterKey<Texture> SubsurfaceLightingAmount = ParameterKeys.NewObject<Texture>();
    public static readonly ValueParameterKey<float> SubsurfaceLightingAmountValue = ParameterKeys.NewValue<float>();

    public static readonly ObjectParameterKey<Texture> Extinction = ParameterKeys.NewObject<Texture>();
    public static readonly ValueParameterKey<float> ExtinctionValue = ParameterKeys.NewValue<float>();
}

[DataContract("MaterialVegetationSubSurfaceScatteringFeature")]
[Display("Subsurface Scattering (Vegetation)")]
public class MaterialVegetationSubSurfaceScatteringFeature : MaterialFeature, IMaterialSubsurfaceScatteringFeature
{
    [NotNull]
    [DataMember(10)]
    [Display("SubsurfaceLightingAmount")]
    public IComputeScalar SubsurfaceLightingAmount { get; set; } = new ComputeTextureScalar();

    [NotNull]
    [DataMember(20)]
    [Display("Extinction")]
    public IComputeScalar Extinction { get; set; } = new ComputeTextureScalar();

    public override void GenerateShader(MaterialGeneratorContext context)
    {
        ClampFloat(SubsurfaceLightingAmount, 0.0f, 1.0f);
        var subsurfaceLightingAmountSource = SubsurfaceLightingAmount.GenerateShaderSource(context, new MaterialComputeColorKeys(SubSurfaceScatteringKeys.SubsurfaceLightingAmount, SubSurfaceScatteringKeys.SubsurfaceLightingAmountValue, Color.White));

        ClampFloat(Extinction, 0.0f, 1.0f);
        var extinctionSource = Extinction.GenerateShaderSource(context, new MaterialComputeColorKeys(SubSurfaceScatteringKeys.Extinction, SubSurfaceScatteringKeys.ExtinctionValue, Color.White));

        var shaderSource = new ShaderMixinSource();
        shaderSource.Mixins.Add(new ShaderClassSource("MaterialVegetationSurfaceSubsurfaceScatteringShading"));
        shaderSource.AddComposition("SubsurfaceLightingAmount", subsurfaceLightingAmountSource);
        shaderSource.AddComposition("Extinction", extinctionSource);

        var shaderBuilder = context.AddShading(this);
        shaderBuilder.LightDependentSurface = shaderSource;
    }

    protected bool Equals(MaterialVegetationSubSurfaceScatteringFeature other)
    {
        return SubsurfaceLightingAmount.Equals(other.SubsurfaceLightingAmount) && Extinction.Equals(other.Extinction);
    }

    public bool Equals(IMaterialShadingModelFeature? other)
    {
        return Equals((MaterialVegetationSubSurfaceScatteringFeature)other!);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((MaterialVegetationSubSurfaceScatteringFeature)obj);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(SubsurfaceLightingAmount, Extinction);
    }

    private static void ClampFloat([NotNull] IComputeScalar key, float min, float max)
    {
        if (key is ComputeFloat asFloat)
            asFloat.Value = MathUtil.Clamp(asFloat.Value, min, max);
    }
}
