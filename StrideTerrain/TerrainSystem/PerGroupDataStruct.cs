﻿using System.Runtime.InteropServices;

namespace StrideTerrain.TerrainSystem;

[StructLayout(LayoutKind.Sequential)]
public struct PerGroupDataStruct
{
    public int Iterations;
    public float DeltaErrorStart;
    public float Padding0;
    public float Padding1;
}
