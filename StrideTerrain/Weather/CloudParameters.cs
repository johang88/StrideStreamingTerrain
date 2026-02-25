using Stride.Core;
using System.Runtime.InteropServices;

namespace StrideTerrain.Weather;

[DataContract]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CloudParameters
{
    [DataMember] public float Scale;
    [DataMember] public float Speed;
    [DataMember] public float Cloudiness;
    [DataMember] public float CirrusAmount;

    public CloudParameters()
    {
        Scale = 0.001f;
        Speed = 0.02f;
        Cloudiness = 0.3f;
        CirrusAmount = 0.5f;
    }
}
