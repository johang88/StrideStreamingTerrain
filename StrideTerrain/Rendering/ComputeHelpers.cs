namespace StrideTerrain.Rendering;
public static class ComputeHelpers
{
    public static int DispatchSize(int tgSize, int numElements)
    {
        var dispatchSize = numElements / tgSize;
        dispatchSize += numElements % tgSize > 0 ? 1 : 0;
        return dispatchSize;
    }
}
