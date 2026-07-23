using Stride.Core;
using Stride.Core.Mathematics;
using System.Runtime.InteropServices;

namespace StrideTerrain.Weather;

[DataContract]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CloudParameters
{
    // row 0
    [DataMember] public float Coverage;         // 0-1 slider
    [DataMember] public float CloudType;         // 0-1: stratus -> cumulus -> cumulonimbus
    [DataMember] public float Precipitation;     // 0-1
    [DataMember] public float WindSpeed;         // m/s

    // row 1
    [DataMember] public Vector2 WindDirection;   // normalized horizontal wind direction
    [DataMember] public float LayerBottom;       // m
    [DataMember] public float LayerThickness;    // m

    // row 2
    [DataMember] public float HighCloudsHeight;  // m
    [DataMember] public float CirrusAmount;      // 0-1
    [DataMember] public float AltoAmount;        // 0-1
    [DataMember] public float SigmaS;            // scattering coefficient, per m (see ctor)

    // row 3
    [DataMember] public float SigmaA;            // absorption coefficient, per m
    [DataMember] public float WeatherMapScale;   // m, world size of one weather map tile
    [DataMember] public float BaseNoiseScale;    // m per base noise tile
    [DataMember] public float DetailNoiseScale;  // m per detail noise tile

    // row 4
    [DataMember] public float HighCloudsScale;   // m per high clouds tile
    [DataMember] public float MaxDistance;       // m, volumetric render distance
    [DataMember] public int StepCount;
    [DataMember] public int LightStepCount;

    public CloudParameters()
    {
        Coverage = 0.5f;
        CloudType = 0.35f;
        Precipitation = 0.0f;
        WindSpeed = 2.0f;

        WindDirection = new Vector2(1.0f, 0.0f);
        LayerBottom = 2000.0f;
        LayerThickness = 4000.0f;

        HighCloudsHeight = 8000.0f;
        CirrusAmount = 0.3f;
        AltoAmount = 0.3f;
        // skygl uses 0.01 per m, but its weather-map presets push in-cloud
        // coverage close to 1 while ours peaks lower, and `density *= coverage`
        // scales the result down — leaving cores at ~0.05 density and visibly
        // see-through. Raised so a normal cloud core is optically thick.
        SigmaS = 0.05f;

        SigmaA = 0.0f;
        WeatherMapScale = 128000.0f;
        BaseNoiseScale = 25000.0f;
        DetailNoiseScale = 1500.0f;

        HighCloudsScale = 32000.0f;
        MaxDistance = 40000.0f;
        StepCount = 64;
        LightStepCount = 6;
    }
}

/// <summary>
/// Parameters for the weather map generation (not sent to GPU cloud cbuffer).
/// </summary>
[DataContract]
public class WeatherMapParameters
{
    [DataMember] public int MapSize { get; set; } = 512;
}
