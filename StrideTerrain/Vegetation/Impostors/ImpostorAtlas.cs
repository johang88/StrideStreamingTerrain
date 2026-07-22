using Stride.Core.Mathematics;
using Stride.Graphics;
using System;

namespace StrideTerrain.Vegetation.Impostors;

/// <summary>
/// A baked hemi-octahedral impostor: GridSize x GridSize frames packed into two atlases.
/// </summary>
public sealed class ImpostorAtlas : IDisposable
{
    /// <summary>Albedo in RGB, coverage in A.</summary>
    public required Texture Diffuse { get; init; }

    /// <summary>Frame-space normal packed to [0,1] in RGB.</summary>
    public required Texture Normal { get; init; }

    public required int GridSize { get; init; }

    public required int FrameResolution { get; init; }

    /// <summary>
    /// World size of the quad the frames were captured with. The billboard has to be drawn at
    /// exactly this size (times instance scale) or the baked silhouette will not match the space
    /// it is drawn into.
    /// </summary>
    public required Vector2 WorldSize { get; init; }

    /// <summary>
    /// Offset from the instance's origin to the centre of the captured bounds, in world units.
    /// Trees pivot at their base, so the billboard has to be lifted by this to sit correctly.
    /// </summary>
    public required Vector3 CenterOffset { get; init; }

    public void Dispose()
    {
        Diffuse.Dispose();
        Normal.Dispose();
    }
}
