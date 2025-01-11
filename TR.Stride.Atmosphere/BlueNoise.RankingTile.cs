namespace TR.Stride.Atmosphere;
internal static partial class BlueNoise
{
    // The ranking tile of 128x128 pixels.
    // Each pixel contains an optimized 8d key used to scramble the sequence.
    // The keys are optimized for all the powers of two spp below 1in 8d.
    // Note ... it's all zeroes ... 
    public static int[] rankingTile = new int[128 * 128 * 8];
}
