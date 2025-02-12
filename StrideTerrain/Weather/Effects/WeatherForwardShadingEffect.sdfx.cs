﻿// <auto-generated>
// Do not edit this file yourself!
//
// This code was generated by Stride Shader Mixin Code Generator.
// To generate it yourself, please install Stride.VisualStudio.Package .vsix
// and re-save the associated .sdfx.
// </auto-generated>

using System;
using Stride.Core;
using Stride.Rendering;
using Stride.Graphics;
using Stride.Shaders;
using Stride.Core.Mathematics;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.Weather.Effects
{
    [DataContract]public partial class WeatherForwardShadingEffectParameters : ShaderMixinParameters
    {
        public static readonly PermutationParameterKey<bool> EnableAerialPerspective = ParameterKeys.NewPermutation<bool>(false);
        public static readonly PermutationParameterKey<bool> EnableVolumetricSunLight = ParameterKeys.NewPermutation<bool>(false);
        public static readonly PermutationParameterKey<bool> EnableHeightFog = ParameterKeys.NewPermutation<bool>(false);
    };
    internal static partial class ShaderMixins
    {
        internal partial class WeatherForwardShadingEffect  : IShaderMixinBuilder
        {
            public void Generate(ShaderMixinSource mixin, ShaderMixinContext context)
            {
                context.Mixin(mixin, "StrideForwardShadingEffect");
                context.Mixin(mixin, "WeatherFordwardRenderer", context.GetParam(WeatherForwardShadingEffectParameters.EnableAerialPerspective), context.GetParam(WeatherForwardShadingEffectParameters.EnableVolumetricSunLight), context.GetParam(WeatherForwardShadingEffectParameters.EnableHeightFog));
            }

            [System.Runtime.CompilerServices.ModuleInitializer]
            internal static void __Initialize__()

            {
                ShaderMixinManager.Register("WeatherForwardShadingEffect", new WeatherForwardShadingEffect());
            }
        }
    }
}
