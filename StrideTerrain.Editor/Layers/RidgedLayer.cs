using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Editor.Effects;
using System;
using System.Buffers;
using static Hexa.NET.ImGui.ImGui;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.Editor.Layers;

[Display("Ridged", "Noise")]
public class RidgedLayer : ITerrainLayerType
{
    public string ShaderName => "LayerRidged";
    public bool IsDirty { get; set; }

    public float Frequency = 0.5f;
    public float Amplitude = 0.35f;
    public float Lacunarity = 2.0f;
    public float Gain = 0.5f;
    public int Octaves = 5;
    public float Seed = 5;
    public float Scale = 1;

    private Buffer? _offsetsBuffer;

    public void DrawUi()
    {
        IsDirty |= DragFloat("Seed", ref Seed);
        IsDirty |= DragFloat("Scale", ref Scale);
        IsDirty |= DragFloat("Frequency", ref Frequency);
        IsDirty |= DragFloat("Amplitude", ref Amplitude);
        IsDirty |= DragFloat("Lacunarity", ref Lacunarity);
        IsDirty |= DragFloat("Gain", ref Gain);
        IsDirty |= InputInt("Octaves", ref Octaves);
        Octaves = Math.Max(1, Octaves);
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
        IsDirty = false;

        if (_offsetsBuffer == null || _offsetsBuffer.Description.SizeInBytes != sizeof(float) * 2 * Octaves)
        {
            _offsetsBuffer?.Dispose();
           _offsetsBuffer = Buffer.Structured.New(renderDrawContext.GraphicsDevice, Octaves, sizeof(float) * 2, true);
        }

        var rng = new RandomSeed();
        var offsets = ArrayPool<float>.Shared.Rent(Octaves * 2);
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = -10000 + rng.GetFloat((uint)(Seed + i)) * 20000;
        }

        _offsetsBuffer.SetData(renderDrawContext.CommandList, (ReadOnlySpan<float>)offsets.AsSpan(0, Octaves * 2));

        parameterCollection.Set(LayerRidgedKeys.Scale, Scale);
        parameterCollection.Set(LayerRidgedKeys.Frequency, Frequency);
        parameterCollection.Set(LayerRidgedKeys.Amplitude, Amplitude);
        parameterCollection.Set(LayerRidgedKeys.Lacunarity, Lacunarity);
        parameterCollection.Set(LayerRidgedKeys.Gain, Gain);
        parameterCollection.Set(LayerRidgedKeys.Octaves, Octaves);
        //parameterCollection.Set(LayerRidgedKeys.Offsets, _offsetsBuffer);
    }
}
