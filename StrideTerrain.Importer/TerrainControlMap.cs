using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace StrideTerrain.Importer;

public static class TerrainControlMap
{
    // Fraction helper
    private static float Frac(float x) => x - MathF.Floor(x);

    // Linear interpolation
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // Smooth 2D noise for large patches
    private static float SmoothNoise2D(Vector2 pos)
    {
        Vector2 ipos = new Vector2(MathF.Floor(pos.X), MathF.Floor(pos.Y));
        Vector2 fpos = new Vector2(Frac(pos.X), Frac(pos.Y));

        float a = Frac(MathF.Sin(Vector2.Dot(ipos, new Vector2(12.9898f, 78.233f))) * 43758.5453f);
        float b = Frac(MathF.Sin(Vector2.Dot(ipos + new Vector2(1, 0), new Vector2(12.9898f, 78.233f))) * 43758.5453f);
        float c = Frac(MathF.Sin(Vector2.Dot(ipos + new Vector2(0, 1), new Vector2(12.9898f, 78.233f))) * 43758.5453f);
        float d = Frac(MathF.Sin(Vector2.Dot(ipos + new Vector2(1, 1), new Vector2(12.9898f, 78.233f))) * 43758.5453f);

        float u = fpos.X;
        float v = fpos.Y;

        float ab = Lerp(a, b, u);
        float cd = Lerp(c, d, u);
        return Lerp(ab, cd, v);
    }

    private static float LowFreqNoise(Vector2 worldXZ, float scale = 0.25f) =>
        SmoothNoise2D(worldXZ * scale);

    // Optional: small height offset to randomize biome bands
    private static float HeightOffset(Vector2 worldXZ, float range = 5f) =>
        (LowFreqNoise(worldXZ) - 0.5f) * 2f * range;

    // Compute slope from normal
    private static float ComputeSlope(Vector3 normal) =>
        Math.Clamp(1f - normal.Y, 0f, 1f);

    // Write control map value for one point
    public static ushort ComputeControlValue(float height, Vector3 normal, Vector3 worldPos)
    {
        float slope = ComputeSlope(normal);
        float rnd = LowFreqNoise(worldPos.XZ());
        float h = height + HeightOffset(worldPos.XZ());

        var textureIndex = 0;
        var scaleIndex = 2;

        // --- 1. Coast / ocean
        if (h < 62f)
        {
            float coastThresh = 0.01f + rnd * 0.01f;
            textureIndex = slope < coastThresh ? 21 : 6;
            scaleIndex = 2;
        }
        // --- 2. Lowlands / forest
        else if ((h + HeightOffset(worldPos.XZ()) * 2) < 140f)
        {
            float flatThresh = 0.01f + rnd * 0.01f;
            float gentleThresh = 0.35f + rnd * 0.05f;
            float steepThresh = 0.55f + rnd * 0.05f;

            if (slope < flatThresh)
            {
                textureIndex = rnd < 0.33f ? 0 : (rnd < 0.66f ? 27 : 28);
            }
            else if (slope < gentleThresh)
            {
                textureIndex = rnd < 0.5f ? 2 : 27;
            }
            else if (slope < steepThresh)
            {
                textureIndex = rnd < 0.5f ? 4 : 18;
            }
            else
            {
                textureIndex = 18;
            }

            scaleIndex = slope > 0.6f ? 3 : 1;
        }
        // --- 3. Mountains / snow
        else
        {
            float mountainNoise = LowFreqNoise(worldPos.XZ() * 0.5f); // coarse scale for large snow patches
            float snowHeightOffset = mountainNoise * 10f - 5f;        // ±5 meters
            float effectiveHeight = h + snowHeightOffset;

            float flatThresh = 0.08f + rnd * 0.02f;
            textureIndex = (slope < flatThresh && effectiveHeight >= 140f) ? 12 : 14;
            scaleIndex = slope > 0.6f ? 3 : 4;
        }

        // Encode into ushort: lower 5 bits textureIndex, upper 3 bits scaleIndex
        return (ushort)((scaleIndex << 5) | (textureIndex & 0x1F));
    }

    // Extension for Vector3.XZ as Vector2
    private static Vector2 XZ(this Vector3 v) => new Vector2(v.X, v.Z);
}