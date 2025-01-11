namespace TR.Stride.Atmosphere;
static internal partial class BlueNoise
{
    public static float SamplerBlueNoiseErrorDistribution_128x128_OptimizedFor_2d2d2d2d_1spp(int pixel_i, int pixel_j, int sampleIndex, int sampleDimension)
    {
        // wrap arguments
        pixel_i = pixel_i & 127;
        pixel_j = pixel_j & 127;
        sampleIndex = sampleIndex & 255;
        sampleDimension = sampleDimension & 255;

        // xor index based on optimized ranking
        int rankedSampleIndex = sampleIndex ^ 0;

        // fetch value in sequence
        int value = sobol_256spp_256d[sampleDimension + rankedSampleIndex * 256];

        // If the dimension is optimized, xor sequence value based on optimized scrambling
        value = value ^ scramblingTile[(sampleDimension % 8) + (pixel_i + pixel_j * 128) * 8];

        // convert to float and return
        float v = (0.5f + value) / 256.0f;
        return v;
    }
}
