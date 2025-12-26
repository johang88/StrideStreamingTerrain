using Stride.Core;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using StrideTerrain.Editor.Effects;
using System;
using System.Collections.Generic;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor.Layers;

[Display("Hydraulic Erosion", "Erosion")]
public class ErosionLayer : ITerrainLayerType, ITerrainLayerCustomDraw
{
    public string ShaderName => "LayerErosion";
    public bool IsDirty { get; set; }

    public int NumErosionIterations = 50000;
    public int ErosionBrushRadius = 3;
    public int MaxLifetime = 30;
    public float SedimentCapacityFactor = 3;
    public float MinSedimentCapacity = .01f;
    public float DepositSpeed = 0.3f;
    public float ErodeSpeed = 0.3f;

    public float EvaporateSpeed = .01f;
    public float Gravity = 4;
    public float StartSpeed = 1;
    public float StartWater = 1;
    public float Inertia = 0.3f;

    public void DrawUi()
    {
        IsDirty |= DragInt("Num Erosion Iterations", ref NumErosionIterations);
        IsDirty |= DragInt("Erosion Brush Radius", ref ErosionBrushRadius);
        IsDirty |= DragInt("Max Lifetime", ref MaxLifetime);
        IsDirty |= DragFloat("Sediment Capacity Factor", ref SedimentCapacityFactor, 0.5f);
        IsDirty |= DragFloat("Min Sediment Capacity", ref MinSedimentCapacity, 0.01f);
        IsDirty |= DragFloat("Deposit Speed", ref DepositSpeed, 0.1f);
        IsDirty |= DragFloat("Erode Speed", ref ErodeSpeed, 0.1f);
        IsDirty |= DragFloat("Evaporate Speed", ref EvaporateSpeed, 0.01f);
        IsDirty |= DragFloat("Gravity", ref Gravity, 0.5f);
        IsDirty |= DragFloat("Start Speed", ref StartSpeed, 0.1f);
        IsDirty |= DragFloat("Start Water", ref StartWater, 0.1f);
        IsDirty |= DragFloat("Inertia", ref Inertia, 0.1f);
    }

    public void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection)
    {
        IsDirty = false;
    }

    public void Draw(TerrainSettings terrainSettings, RenderDrawContext renderDrawContext, ComputeEffectShader shader, Texture input, Texture output)
    {
        renderDrawContext.CommandList.Copy(input, output);

        var numThreads = NumErosionIterations / 1024;

        shader.ThreadGroupCounts = new(numThreads, 1, 1);
        shader.ThreadNumbers = new(1024, 1, 1);

        // Create brush
        var brushIndexOffsets = new List<int>();
        var brushWeights = new List<float>();

        float weightSum = 0;
        for (int brushY = -ErosionBrushRadius; brushY <= ErosionBrushRadius; brushY++)
        {
            for (int brushX = -ErosionBrushRadius; brushX <= ErosionBrushRadius; brushX++)
            {
                float sqrDst = brushX * brushX + brushY * brushY;
                if (sqrDst < ErosionBrushRadius * ErosionBrushRadius)
                {
                    brushIndexOffsets.Add(brushY * terrainSettings.Resolution + brushX);
                    float brushWeight = 1 - MathF.Sqrt(sqrDst) / ErosionBrushRadius;
                    weightSum += brushWeight;
                    brushWeights.Add(brushWeight);
                }
            }
        }
        for (int i = 0; i < brushWeights.Count; i++)
        {
            brushWeights[i] /= weightSum;
        }

        // Upload brush data
        using var brushIndexOffsetsBuffer = Stride.Graphics.Buffer.Structured.New(renderDrawContext.GraphicsDevice, brushIndexOffsets.ToArray(), true);
        using var brushWeightsBuffer = Stride.Graphics.Buffer.Structured.New(renderDrawContext.GraphicsDevice, brushWeights.ToArray(), true);

        shader.Parameters.Set(LayerErosionKeys.BrushIndices, brushIndexOffsetsBuffer);
        shader.Parameters.Set(LayerErosionKeys.BrushWeights, brushWeightsBuffer);

        // Genereate random indices for drop placement
        var rng = new Random();
        var randomIndices = new int[NumErosionIterations];

        var passes = terrainSettings.Resolution / 256;
        passes = passes * passes;
        passes = 1;
        for (var n = 0; n < passes; n++)
        {
            for (int i = 0; i < NumErosionIterations; i++)
            {
                int randomX = rng.Next(ErosionBrushRadius, terrainSettings.Resolution + ErosionBrushRadius + 1);
                int randomY = rng.Next(ErosionBrushRadius, terrainSettings.Resolution + ErosionBrushRadius + 1);
                randomIndices[i] = randomY * terrainSettings.Resolution + randomX;
            }

            using var randomIndicesBuffer = Stride.Graphics.Buffer.Structured.New(renderDrawContext.GraphicsDevice, randomIndices, true);

            shader.Parameters.Set(LayerErosionKeys.RandomIndices, randomIndicesBuffer);

            shader.Parameters.Set(LayerErosionKeys.BrushLength, brushIndexOffsets.Count);
            shader.Parameters.Set(LayerErosionKeys.BorderSize, ErosionBrushRadius);
            shader.Parameters.Set(LayerErosionKeys.MaxLifetime, MaxLifetime);
            shader.Parameters.Set(LayerErosionKeys.Inertia, Inertia);
            shader.Parameters.Set(LayerErosionKeys.SedimentCapacityFactor, SedimentCapacityFactor);
            shader.Parameters.Set(LayerErosionKeys.MinSedimentCapacity, MinSedimentCapacity);
            shader.Parameters.Set(LayerErosionKeys.DepositSpeed, DepositSpeed);
            shader.Parameters.Set(LayerErosionKeys.ErodeSpeed, ErodeSpeed);
            shader.Parameters.Set(LayerErosionKeys.EvaporateSpeed, EvaporateSpeed);
            shader.Parameters.Set(LayerErosionKeys.Gravity, Gravity);
            shader.Parameters.Set(LayerErosionKeys.StartSpeed, StartSpeed);
            shader.Parameters.Set(LayerErosionKeys.StartWater, StartWater);

            shader.Draw(renderDrawContext, "Erosion");
        }
    }
}
