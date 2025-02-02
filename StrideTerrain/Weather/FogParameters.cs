using Stride.Core.Mathematics;
using System.Runtime.InteropServices;

namespace StrideTerrain.Weather;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct FogParameters
{
    public float Start;
    public float HeightStart;
    public float HeightEnd;
    public float Density;
    public Vector3 Color;
    public float OverrideFogColor;

    public FogParameters()
    {
        Start = 500.0f;
        HeightStart = 0.0f;
        HeightEnd = 200.0f;
        Density = 0.1f;
        Color = Vector3.One;
        OverrideFogColor = 1;
    }
}
