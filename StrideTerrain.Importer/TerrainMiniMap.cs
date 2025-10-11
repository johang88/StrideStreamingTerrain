using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;

namespace StrideTerrain.Importer;
public static class TerrainMiniMap
{
    public static void Generate(
         int mapSize,
         float invUnitsPerTexel,
         int terrainSize,
         Func<int, int, float> HeightAt,
         Func<int, int, ushort> ControlAt,
         List<TreeInstance> trees,
         string outputPath,
         float waterHeight = 62f)
    {
        int bytesPerPixel = 4;
        byte[] pixels = new byte[mapSize * mapSize * bytesPerPixel];

        Vector3[,] colorBuffer = new Vector3[mapSize, mapSize];

        // --- 1. Sample terrain colors
        for (int y = 0; y < mapSize; y++)
        {
            for (int x = 0; x < mapSize; x++)
            {
                ushort control = ControlAt(x, y);
                int textureIndex = control & 0x1F;
                float height = HeightAt(x, y);

                Vector3 baseColor = textureIndex switch
                {
                    0 => new Vector3(0.18f, 0.45f, 0.12f),
                    2 => new Vector3(0.25f, 0.55f, 0.15f),
                    4 => new Vector3(0.35f, 0.35f, 0.28f),
                    6 => new Vector3(0.60f, 0.55f, 0.35f),
                    12 => new Vector3(0.95f, 0.95f, 0.95f),
                    14 => new Vector3(0.45f, 0.45f, 0.45f),
                    17 => new Vector3(0.20f, 0.40f, 0.10f),
                    18 => new Vector3(0.25f, 0.25f, 0.20f),
                    21 => new Vector3(0.55f, 0.55f, 0.45f),
                    27 => new Vector3(0.16f, 0.35f, 0.12f),
                    _ => new Vector3(0.3f, 0.4f, 0.25f)
                };

                // Add subtle height brightness
                float brightness = Math.Clamp((height - waterHeight) / 200f, 0f, 1f);
                baseColor *= 0.6f + brightness * 0.4f;

                // Add water overlay below surface
                if (height < waterHeight + 4f) // fade out 4m above coast
                {
                    float depth = waterHeight - height;
                    float t = Math.Clamp(depth / 10f, 0f, 1f); // 0–10 m range
                    Vector3 shallowColor = new(0.15f, 0.35f, 0.55f);
                    Vector3 deepColor = new(0.05f, 0.15f, 0.35f);
                    Vector3 waterColor = Vector3.Lerp(shallowColor, deepColor, t);

                    // Blend with terrain — stronger blend for deeper areas
                    float blend = MathF.Pow(t, 0.7f);
                    baseColor = Vector3.Lerp(baseColor, waterColor, blend);

                    // Add subtle coastline brightening
                    if (depth < 1f)
                        baseColor += new Vector3(0.02f, 0.03f, 0.04f) * (1f - depth);
                }

                colorBuffer[x, y] = baseColor;
            }
        }

        // --- 3. Apply simple 3x3 smoothing kernel
        float[,] kernel = {
            {1f,2f,1f},
            {2f,4f,2f},
            {1f,2f,1f}
        };
        float kernelSum = 16f;

        for (int y = 1; y < mapSize - 1; y++)
        {
            for (int x = 1; x < mapSize - 1; x++)
            {
                Vector3 accum = Vector3.Zero;
                for (int ky = -1; ky <= 1; ky++)
                {
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        accum += colorBuffer[x + kx, y + ky] * kernel[ky + 1, kx + 1];
                    }
                }
                colorBuffer[x, y] = accum / kernelSum;
            }
        }

        // --- 4. Overlay trees
        foreach (var tree in trees)
        {
            int px = (int)((tree.X * invUnitsPerTexel) / terrainSize * mapSize);
            int py = (int)((tree.Z * invUnitsPerTexel) / terrainSize * mapSize);

            int radius = tree.Type switch
            {
                0 or 1 => 1,
                2 => 2,
                _ => 3
            };

            for (int dy = -radius; dy <= radius; dy++)
            {
                int yy = py + dy;
                if (yy < 0 || yy >= mapSize) continue;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    int xx = px + dx;
                    if (xx < 0 || xx >= mapSize) continue;

                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > radius) continue;

                    float alpha = 0.7f * (1f - dist / radius);
                    Vector3 treeColor = new Vector3(0.1f, 0.5f, 0.1f);

                    colorBuffer[xx, yy] = Vector3.Lerp(colorBuffer[xx, yy], treeColor, alpha);
                }
            }
        }

        // --- 5. Convert to byte array (BGRA)
        for (int y = 0; y < mapSize; y++)
        {
            for (int x = 0; x < mapSize; x++)
            {
                int idx = (y * mapSize + x) * bytesPerPixel;
                Vector3 c = colorBuffer[x, y];

                pixels[idx + 0] = (byte)(Math.Clamp(c.Z, 0f, 1f) * 255); // B
                pixels[idx + 1] = (byte)(Math.Clamp(c.Y, 0f, 1f) * 255); // G
                pixels[idx + 2] = (byte)(Math.Clamp(c.X, 0f, 1f) * 255); // R
                pixels[idx + 3] = 255;                                     // A
            }
        }

        // --- 5. Save as PNG
        using var bmp = new Bitmap(mapSize, mapSize, PixelFormat.Format32bppArgb);
        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, mapSize, mapSize),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
        bmp.UnlockBits(bmpData);
        bmp.Save(outputPath, ImageFormat.Png);
    }
}
