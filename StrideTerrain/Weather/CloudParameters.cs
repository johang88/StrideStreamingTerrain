using Stride.Core;
using Stride.Core.Mathematics;
using System.Runtime.InteropServices;

namespace StrideTerrain.Weather;

[DataContract]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CloudParameters
{
    // Shape
    [DataMember] public float Coverage;
    [DataMember] public float Density;
    [DataMember] public float BaseNoiseScale;
    [DataMember] public float DetailNoiseScale;

    [DataMember] public float ErosionStrength;
    [DataMember] public float WindSpeed;
    [DataMember] public Vector2 WindDirection;

    // Layer geometry (meters)
    [DataMember] public float CloudLayerMin;
    [DataMember] public float CloudLayerMax;

    // Lighting
    [DataMember] public float PhaseG;
    [DataMember] public float LightAbsorption;

    // Quality
    [DataMember] public int StepCount;
    [DataMember] public int LightStepCount;

    // Cirrus
    [DataMember] public float CirrusAmount;
    [DataMember] public float _Padding0;

    public CloudParameters()
    {
        Coverage = 0.6f;
        Density = 2.0f;
        BaseNoiseScale = 0.00005f;
        DetailNoiseScale = 0.0005f;
        ErosionStrength = 0.25f;
        WindSpeed = 10.0f;
        WindDirection = new Vector2(1.0f, 0.0f);
        CloudLayerMin = 1500.0f;
        CloudLayerMax = 4000.0f;
        PhaseG = 0.8f;
        LightAbsorption = 0.35f;
        StepCount = 64;
        LightStepCount = 6;
        CirrusAmount = 0.5f;
        _Padding0 = 0.0f;
    }
}

/// <summary>
/// Parameters for the weather map generation (not sent to GPU cloud cbuffer).
/// </summary>
[DataContract]
public class WeatherMapParameters
{
    [DataMember] public float CoverageScale { get; set; } = 0.02f;
    [DataMember] public float TypeScale { get; set; } = 0.01f;
    [DataMember] public int MapSize { get; set; } = 512;
}
