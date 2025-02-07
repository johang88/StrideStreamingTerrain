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

namespace TR.Stride.Atmosphere
{
    public static partial class CloudRayMarchingKeys
    {
        public static readonly ValueParameterKey<float> CloudStartHeight = ParameterKeys.NewValue<float>(6361.0f);
        public static readonly ValueParameterKey<float> CloudThickness = ParameterKeys.NewValue<float>(10.0f);
        public static readonly ValueParameterKey<float> CloudMaxMarchingDistance = ParameterKeys.NewValue<float>(50.0f);
        public static readonly ValueParameterKey<float> CloudTracingMaxStartDistance = ParameterKeys.NewValue<float>(350.0f);
        public static readonly ValueParameterKey<uint> CloudStepCount = ParameterKeys.NewValue<uint>(128);
        public static readonly ValueParameterKey<float> CloudPhaseForward = ParameterKeys.NewValue<float>(0.5f);
        public static readonly ValueParameterKey<float> CloudPhaseBackward = ParameterKeys.NewValue<float>(-0.5f);
        public static readonly ValueParameterKey<float> CloudPhaseMixFactor = ParameterKeys.NewValue<float>(0.5f);
        public static readonly ValueParameterKey<float> CloudMultiScatterExtinction = ParameterKeys.NewValue<float>(0.175f);
        public static readonly ValueParameterKey<float> CloudMultiScatterScatter = ParameterKeys.NewValue<float>(1.0f);
        public static readonly ValueParameterKey<bool> CloudEnableGroundContribution = ParameterKeys.NewValue<bool>(true);
        public static readonly ValueParameterKey<float> CloudPowderPow = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<float> CloudPowderScale = ParameterKeys.NewValue<float>(1);
        public static readonly ValueParameterKey<float> CloudNoiseScale = ParameterKeys.NewValue<float>(0.01f);
        public static readonly ValueParameterKey<float> CloudFogFade = ParameterKeys.NewValue<float>(1.0f);
        public static readonly ValueParameterKey<float> Time = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<uint> FrameIndex = ParameterKeys.NewValue<uint>();
        public static readonly ValueParameterKey<float> CloudCoverage = ParameterKeys.NewValue<float>(0.3f);
        public static readonly ValueParameterKey<float> CloudDensity = ParameterKeys.NewValue<float>(1.0f);
        public static readonly ValueParameterKey<float> CloudSpeed = ParameterKeys.NewValue<float>(0.05f);
        public static readonly ValueParameterKey<Vector3> WindDirection = ParameterKeys.NewValue<Vector3>(new Vector3(1,0,0));
        public static readonly ValueParameterKey<Vector2> CloudWeatherUvScale = ParameterKeys.NewValue<Vector2>(new Vector2(0.02f,0.02f));
        public static readonly ValueParameterKey<float> CloudBasicNoiseScale = ParameterKeys.NewValue<float>(0.3f);
        public static readonly ValueParameterKey<float> CloudDetailNoiseScale = ParameterKeys.NewValue<float>(0.6f);
        public static readonly ValueParameterKey<bool> ApplyRandomOffset = ParameterKeys.NewValue<bool>(true);
        public static readonly ValueParameterKey<Matrix> ViewProjectionMatrix = ParameterKeys.NewValue<Matrix>();
        public static readonly ValueParameterKey<bool> WriteDepth = ParameterKeys.NewValue<bool>(false);
        public static readonly ObjectParameterKey<Texture> CloudColorTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> CloudDepthTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> CloudCurlNoiseTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> BasicNoiseTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> DetailNoiseTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> WeatherTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> BlueNoiseTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<SamplerState> SamplerLinearRepeat = ParameterKeys.NewObject<SamplerState>();
    }
}
