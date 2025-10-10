using System.Numerics;

namespace StrideTerrain.Importer;

public static class Noise
{
    // Fraction helper
    private static float Frac(float x) => x - MathF.Floor(x);

    // Linear interpolation
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // Smooth 2D noise (bilinear interpolation)
    public static float SmoothNoise2D(Vector2 pos)
    {
        // Integer cell coordinates
        Vector2 ipos = new Vector2(MathF.Floor(pos.X), MathF.Floor(pos.Y));
        Vector2 fpos = new Vector2(Frac(pos.X), Frac(pos.Y));

        // Random values at four corners
        float a = Frac(MathF.Sin(Vector2.Dot(ipos, new Vector2(12.9898f, 78.233f))) * 43758.5453f);
        float b = Frac(MathF.Sin(Vector2.Dot(ipos + new Vector2(1, 0), new Vector2(12.9898f, 78.233f))) * 43758.5453f);
        float c = Frac(MathF.Sin(Vector2.Dot(ipos + new Vector2(0, 1), new Vector2(12.9898f, 78.233f))) * 43758.5453f);
        float d = Frac(MathF.Sin(Vector2.Dot(ipos + new Vector2(1, 1), new Vector2(12.9898f, 78.233f))) * 43758.5453f);

        // Bilinear interpolation
        float u = fpos.X;
        float v = fpos.Y;
        float ab = Lerp(a, b, u);
        float cd = Lerp(c, d, u);
        return Lerp(ab, cd, v);
    }

    // Low frequency noise for large patches
    public static float LowFreqNoise(Vector2 worldXZ, float scale = 0.25f)
    {
        return SmoothNoise2D(worldXZ * scale);
    }
}
