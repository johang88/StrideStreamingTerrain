using Stride.Core;
using Stride.Rendering;
using StrideTerrain.Editor.Effects;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor.Layers;

[Display("Turbulence", "Noise")]
public class TurbulenceLayer : ITerrainLayerType
{
    public string ShaderName => "LayerTurbulence";
    public bool IsDirty { get; set; }

    public float Seed = 5;
    public float Frequency = 5;
    public float Amplitude = 0.5f;
    public float Lacunarity = 2.0f;
    public float Gain = 0.5f;
    public int Octaves = 5;

    public void DrawUi()
    {
        IsDirty |= DragFloat("Seed", ref Frequency);
        IsDirty |= DragFloat("Frequency", ref Frequency);
        IsDirty |= DragFloat("Amplitude", ref Amplitude);
        IsDirty |= DragFloat("Lacunarity", ref Lacunarity);
        IsDirty |= DragFloat("Gain", ref Gain);
        IsDirty |= InputInt("Octaves", ref Octaves);
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
        IsDirty = false;

        parameterCollection.Set(LayerTurbulenceKeys.Seed, Seed);
        parameterCollection.Set(LayerTurbulenceKeys.Frequency, Frequency);
        parameterCollection.Set(LayerTurbulenceKeys.Amplitude, Amplitude);
        parameterCollection.Set(LayerTurbulenceKeys.Lacunarity, Lacunarity);
        parameterCollection.Set(LayerTurbulenceKeys.Gain, Gain);
        parameterCollection.Set(LayerTurbulenceKeys.Octaves, Octaves);
    }
}
