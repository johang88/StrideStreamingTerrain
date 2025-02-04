using Stride.Core;
using Stride.Core.Mathematics;
using System.Runtime.InteropServices;

namespace StrideTerrain.Weather;

[DataContract]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct FogParameters
{
    [DataMember] public float Start;
    [DataMember] public float HeightStart;
    [DataMember] public float HeightEnd;
    [DataMember] public float Density;
    [DataMember] public Vector3 Color;
    [DataMember] public float OverrideFogColor;

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
