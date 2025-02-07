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

namespace TR.Stride.Ocean
{
    public static partial class OceanShadingCommonKeys
    {
        public static readonly ValueParameterKey<Color3> Color = ParameterKeys.NewValue<Color3>();
        public static readonly ValueParameterKey<int> Lod = ParameterKeys.NewValue<int>();
        public static readonly ValueParameterKey<float> LodScale = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<float> LengthScale0 = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> LengthScale1 = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> LengthScale2 = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> SSSBase = ParameterKeys.NewValue<float>(0);
        public static readonly ValueParameterKey<float> SSSScale = ParameterKeys.NewValue<float>(4);
        public static readonly ValueParameterKey<float> SSSStrength = ParameterKeys.NewValue<float>(0.2f);
        public static readonly ValueParameterKey<Color4> SSSColor = ParameterKeys.NewValue<Color4>(new Color4(1,1,1,1));
        public static readonly ValueParameterKey<float> FoamBiasLOD0 = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<float> FoamBiasLOD1 = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<float> FoamBiasLOD2 = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<float> FoamScale = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<float> ContactFoam = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<Color4> FoamColor = ParameterKeys.NewValue<Color4>(new Color4(1,1,1,1));
        public static readonly ValueParameterKey<float> Roughness = ParameterKeys.NewValue<float>(0);
        public static readonly ValueParameterKey<float> RoughnessScale = ParameterKeys.NewValue<float>(0.1f);
        public static readonly ValueParameterKey<float> MaxGloss = ParameterKeys.NewValue<float>(0);
        public static readonly ValueParameterKey<Vector3> LightDirectionWS = ParameterKeys.NewValue<Vector3>();
        public static readonly ValueParameterKey<Color3> ShoreColor = ParameterKeys.NewValue<Color3>(new Color3(1,1,1));
        public static readonly ValueParameterKey<float> RefractionStrength = ParameterKeys.NewValue<float>(50);
        public static readonly ValueParameterKey<float> RefractionDistanceMultiplier = ParameterKeys.NewValue<float>(0.02f);
        public static readonly ValueParameterKey<Color3> Albedo = ParameterKeys.NewValue<Color3>(new Color3(0,0,0));
        public static readonly ValueParameterKey<Color3> Extinction = ParameterKeys.NewValue<Color3>(new Color3(0.7f,0.3f,0.1f));
        public static readonly ObjectParameterKey<Texture> Displacement_c0 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Derivatives_c0 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Turbulence_c0 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Displacement_c1 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Derivatives_c1 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Turbulence_c1 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Displacement_c2 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Derivatives_c2 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> Turbulence_c2 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> FoamTexture = ParameterKeys.NewObject<Texture>();
    }
}
