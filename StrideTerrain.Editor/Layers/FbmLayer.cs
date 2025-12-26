using Stride.Core;
using Stride.Rendering;
using StrideTerrain.Editor.Effects;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor.Layers;

[Display("Fbm", "Noise")]
public class FbmLayer : ITerrainLayerType
{
    public string ShaderName => "LayerFbm";
    public bool IsDirty { get; set; }

    public float Seed = 5;
    public float Frequency = 5;
    public float Amplitude = 0.5f;
    public float Lacunarity = 2.0f;
    public float Gain = 0.5f;
    public int Octaves = 5;

    public void DrawUi()
    {
        IsDirty |= DragFloat("Seed", ref Seed);
        IsDirty |= DragFloat("Frequency", ref Frequency);
        IsDirty |= DragFloat("Amplitude", ref Amplitude);
        IsDirty |= DragFloat("Lacunarity", ref Lacunarity);
        IsDirty |= DragFloat("Gain", ref Gain);
        IsDirty |= InputInt("Octaves", ref Octaves);
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
        IsDirty = false;

        parameterCollection.Set(LayerFbmKeys.Seed, Seed);
        parameterCollection.Set(LayerFbmKeys.Frequency, Frequency);
        parameterCollection.Set(LayerFbmKeys.Amplitude, Amplitude);
        parameterCollection.Set(LayerFbmKeys.Lacunarity, Lacunarity);
        parameterCollection.Set(LayerFbmKeys.Gain, Gain);
        parameterCollection.Set(LayerFbmKeys.Octaves, Octaves);
    }
}
