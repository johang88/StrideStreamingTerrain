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
    public static partial class OceanInitialSpectrumCommonKeys
    {
        public static readonly ObjectParameterKey<Texture> H0 = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> WavesData = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> H0K = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> NoiseTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ValueParameterKey<uint> Size = ParameterKeys.NewValue<uint>();
        public static readonly ValueParameterKey<float> LengthScale = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> CutoffHigh = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> CutoffLow = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> GravityAcceleration = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> Depth = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<SpectrumSettings> Spectrums = ParameterKeys.NewValue<SpectrumSettings>();
    }
}
