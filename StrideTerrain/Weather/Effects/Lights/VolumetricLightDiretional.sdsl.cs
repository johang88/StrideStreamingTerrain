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

namespace StrideTerrain.Weather.Effects.Lights
{
    public static partial class VolumetricLightDiretionalKeys
    {
        public static readonly ObjectParameterKey<Texture> TransmittanceLUT = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<Texture> DepthTexture = ParameterKeys.NewObject<Texture>();
        public static readonly ValueParameterKey<AtmosphereParameters> Atmosphere = ParameterKeys.NewValue<AtmosphereParameters>();
        public static readonly ValueParameterKey<FogParameters> Fog = ParameterKeys.NewValue<FogParameters>();
        public static readonly ValueParameterKey<Matrix> InvViewProjection = ParameterKeys.NewValue<Matrix>();
        public static readonly ValueParameterKey<Vector3> SunDirection = ParameterKeys.NewValue<Vector3>();
        public static readonly ValueParameterKey<Color3> SunColor = ParameterKeys.NewValue<Color3>();
        public static readonly ValueParameterKey<Vector3> CameraPosition = ParameterKeys.NewValue<Vector3>();
        public static readonly ValueParameterKey<Vector2> InvResolution = ParameterKeys.NewValue<Vector2>();
    }
}
