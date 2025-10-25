using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;
using System.Security.Cryptography;

namespace StrideTerrain.Vegetation;

/// <summary>
/// Used for all ground clatter in a limited distance around the player, dynamically scattered at runtime using a compute shader.
/// </summary>
[DefaultEntityComponentRenderer(typeof(GrassProcessor))]
public class GrassComponent : ScriptComponent
{
    public ModelComponent? Model { get; set; }
    public int Size { get; set; } = 128;
    public int Seed { get; set; } = RandomNumberGenerator.GetInt32(int.MaxValue);
    public float CellSize { get; set; } = 1.0f;
    public float MinScale { get; set; } = 0.8f;
    public float MaxScale { get; set; } = 1.2f;
    public float Clumpiness { get; set; } = 0.0f;
    public float ClumpSize { get; set; } = 32.0f;
    public float FadeStartFraction { get; set; } = 0.8f;
    public bool RotateAlongTerrainNormal { get; set; } = true;
    public uint[] ValidBackgroundTexturesIds { get; set; } = new uint[5];

    [Display(category: "Debug")]
    public bool FreezeCameraFrustum { get; set; } = false;
}
