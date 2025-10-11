using Silk.NET.Core.Native;
using StrideTerrain.Common;
using System.Drawing;
using System.Drawing.Imaging;

namespace StrideTerrain.Importer;

public class BiomeMap
{
    public static byte[] Generate(
        int terrainSize,
        int biomeSize,
        float invUnitsPerTexel,
        Func<int, int, float> HeightAt,
         Func<int, int, ushort> ControlAt,
        float waterHeight,
        List<TreeInstance> trees,
        string? debugPngPath = null)
    {
        var biomeMap = new byte[biomeSize * biomeSize];
        var scale = (float)terrainSize / biomeSize;

        for (int y = 0; y < biomeSize; y++)
        {
            for (int x = 0; x < biomeSize; x++)
            {
                int sx = (int)(x * scale);
                int sy = (int)(y * scale);

                float h = HeightAt(sx, sy);

                // Slope check
                float slope = 0;
                if (sx > 0 && sy > 0 && sx < terrainSize - 1 && sy < terrainSize - 1)
                {
                    float dx = HeightAt(sx + 1, sy) - HeightAt(sx - 1, sy);
                    float dy = HeightAt(sx, sy + 1) - HeightAt(sx, sy - 1);
                    slope = MathF.Abs(dx) + MathF.Abs(dy);
                }

                biomeMap[y * biomeSize + x] = (byte)Classify(h, slope, waterHeight);
            }
        }

        // Overlay treees
        foreach (var tree in trees)
        {
            int px = (int)((tree.X * invUnitsPerTexel) / terrainSize * biomeSize);
            int py = (int)((tree.Z * invUnitsPerTexel) / terrainSize * biomeSize);

            int radius = tree.Type switch
            {
                0 or 1 => 1,
                2 => 2,
                _ => 3
            };

            for (int dy = -radius; dy <= radius; dy++)
            {
                int yy = py + dy;
                if (yy < 0 || yy >= biomeSize) continue;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    int xx = px + dx;
                    if (xx < 0 || xx >= biomeSize) continue;

                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > radius) continue;

                    biomeMap[yy * biomeSize + xx] = (byte)BiomeType.Forest;
                }
            }
        }

        //SmoothForests(biomeMap, biomeSize, 3, 15);
        //SmoothForests(biomeMap, biomeSize, 2);

        // Optional visualization
        if (debugPngPath != null)
            SaveBiomeDebugPNG(biomeMap, biomeSize, debugPngPath);

        return biomeMap;
    }

    private static BiomeType Classify(float h, float slope, float waterHeight)
    {
        if (h < waterHeight - 1f)
            return BiomeType.Ocean;
        else if (h < waterHeight + 5f)
            return BiomeType.Beach;
        else if (h > 140)
            return BiomeType.Mountain;
        else
            return BiomeType.Plains;
    }

    private static void SmoothForests(byte[] map, int size, int radius = 3, int forestThreshold = 16)
    {
        var copy = (byte[])map.Clone();

        for (int y = radius; y < size - radius; y++)
        {
            for (int x = radius; x < size - radius; x++)
            {
                int idx = y * size + x;
                if (copy[idx] != (byte)BiomeType.Plains)
                    continue; // Only expand forests into plains

                int forestCount = 0;
                int total = 0;

                for (int ky = -radius; ky <= radius; ky++)
                {
                    for (int kx = -radius; kx <= radius; kx++)
                    {
                        total++;
                        if (copy[(y + ky) * size + (x + kx)] == (byte)BiomeType.Forest)
                            forestCount++;
                    }
                }

                if (forestCount >= forestThreshold)
                    map[idx] = (byte)BiomeType.Forest;
            }
        }
    }

    private static void SaveBiomeDebugPNG(byte[] map, int size, string path)
    {
        int bpp = 4;
        byte[] pixels = new byte[size * size * bpp];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * bpp;
                var c = map[y * size + x] switch
                {
                    (byte)BiomeType.Ocean => Color.FromArgb(255, 40, 80, 200),
                    (byte)BiomeType.Beach => Color.FromArgb(255, 235, 220, 120),
                    (byte)BiomeType.Plains => Color.FromArgb(255, 80, 180, 80),
                    (byte)BiomeType.Forest => Color.FromArgb(255, 40, 120, 40),
                    (byte)BiomeType.Mountain => Color.FromArgb(255, 160, 160, 160),
                    _ => Color.Magenta
                };
                pixels[idx + 0] = c.B;
                pixels[idx + 1] = c.G;
                pixels[idx + 2] = c.R;
                pixels[idx + 3] = 255;
            }
        }

        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bmp.UnlockBits(data);
        bmp.Save(path, ImageFormat.Png);
    }
}
