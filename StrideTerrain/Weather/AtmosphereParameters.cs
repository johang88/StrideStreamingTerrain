using Stride.Core;
using Stride.Core.Mathematics;
using System.Runtime.InteropServices;

namespace StrideTerrain.Weather;

[DataContract]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AtmosphereParameters
{
    // Radius of the planet (center to ground)
    [DataMember] public float BottomRadius;
    // Maximum considered atmosphere height (center to atmosphere top)
    [DataMember] public float TopRadius;

    [DataMemberIgnore] public Vector2 Padding0;

    [DataMember] public Vector3 PlanetCenter;

    // Rayleigh scattering exponential distribution scale in the atmosphere
    [DataMember] public float RayleighDensityExpScale;
    // Rayleigh scattering coefficients
    [DataMember] public Vector3 RayleighScattering;

    // Mie scattering exponential distribution scale in the atmosphere
    [DataMember] public float MieDensityExpScale;
    // Mie scattering coefficients
    [DataMember] public Vector3 MieScattering;
    [DataMemberIgnore] public float Padding1;
    // Mie extinction coefficients
    [DataMember] public Vector3 MieExtinction;
    [DataMemberIgnore] public float Padding2;
    // Mie absorption coefficients
    [DataMember] public Vector3 MieAbsorption;
    // Mie phase function excentricity
    [DataMember] public float MiePhaseG;

    // Another medium type in the atmosphere
    [DataMember] public float AbsorptionDensity0LayerWidth;
    [DataMember] public float AbsorptionDensity0ConstantTerm;
    [DataMember] public float AbsorptionDensity0LinearTerm;
    [DataMember] public float AbsorptionDensity1ConstantTerm;
    // This other medium only absorb light, e.g. useful to represent ozone in the earth atmosphere
    [DataMember] public Vector3 AbsorptionExtinction;
    [DataMember] public float AbsorptionDensity1LinearTerm;

    // The albedo of the ground.
    [DataMember] public Vector3 GroundAlbedo;
    [DataMemberIgnore] public float Padding3;

    [DataMember] public Vector2 RayMarchMinMaxSPP;
    [DataMember] public float DistanceSPPMaxInv;

    // Artist controlled distance scale of aerial perspective
    [DataMember] public float AerialPerspectiveScale;

    public AtmosphereParameters()
    {
        BottomRadius = 6360;
        TopRadius = 6460;
        RayleighDensityExpScale = -1.0f / 8.0f;
        MieDensityExpScale = -1.0f / 1.2f;
        AbsorptionDensity0LayerWidth = 25.0f;
        AbsorptionDensity0ConstantTerm = -2.0f / 3.0f;
        AbsorptionDensity0LinearTerm = 1.0f / 15.0f;
        AbsorptionDensity1ConstantTerm = 8.0f / 3.0f;
        AbsorptionDensity1LinearTerm = -1.0f / 15.0f;
        MiePhaseG = 0.8f;
        RayleighScattering = new(0.005802f, 0.013558f, 0.033100f);
        MieScattering = new(0.003996f, 0.003996f, 0.003996f);
        MieExtinction = new(0.004440f, 0.004440f, 0.004440f);
        AbsorptionExtinction = new(0.000650f, 0.001881f, 0.000085f);
        GroundAlbedo = new(0.3f, 0.3f, 0.3f); // 0.3 for earths ground albedo, see https://nssdc.gsfc.nasa.gov/planetary/factsheet/earthfact.html
        RayMarchMinMaxSPP = new(4, 14);
        DistanceSPPMaxInv = 0.01f;
        AerialPerspectiveScale = 1.0f;

        MieAbsorption.X = MieExtinction.X - MieScattering.X;
        MieAbsorption.Y = MieExtinction.Y - MieScattering.Y;
        MieAbsorption.Z = MieExtinction.Z - MieScattering.Z;
        PlanetCenter = new(0.0f, -BottomRadius - 0.1f, 0.0f); // Spawn 100m in the air
    }
}
