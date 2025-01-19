using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Storage;
using Stride.Rendering;
using Stride.Rendering.UI;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Effects.Material;
using System;
using System.Buffers;
using System.Collections.Generic;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace StrideTerrain.TerrainSystem.Rendering;

public class TerrainRenderFeature : SubRenderFeature
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Terrain.Draw");
    private static readonly ProfilingKey ProflingKeyCull = new("Terrain.Cull");

    [DataMemberIgnore]
    public static readonly PropertyKey<Dictionary<RenderModel, TerrainRuntimeData>> ModelToTerrainMap = new("TerrainRenderFeature.ModelToTerrainMap", typeof(TerrainRenderFeature));
    public static readonly PropertyKey<List<TerrainRuntimeData>> TerrainList = new("TerrainRenderFeature.Terrains", typeof(TerrainRenderFeature));

    private ConstantBufferOffsetReference _chunkSizeOffset;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _chunkSizeOffset = ((RootEffectRenderFeature)RootRenderFeature).CreateFrameCBufferOffsetSlot(TerrainDataKeys.ChunkSize.Name);
    }

    public override void Extract()
    {
        if (Context.VisibilityGroup == null || !Context.VisibilityGroup.Tags.TryGetValue(ModelToTerrainMap, out var modelToTerrainMap))
            return;

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            if (objectNode.RenderObject is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            if (!modelToTerrainMap.TryGetValue(renderModel, out var terrainRenderData))
            {
                continue;
            }

            renderMesh.Enabled = true;
            renderMesh.ProfilingKey = ProfilingKeyDraw;

            break; // Currently only support single terrain
        }
    }

    public override unsafe void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        if (Context.VisibilityGroup == null || !Context.VisibilityGroup.Tags.TryGetValue(ModelToTerrainMap, out var modelToTerrainMap))
            return;

        // It's a bit ugly but a convenient to get data to the shadow map renderer.
        if (!context.Tags.TryGetValue(TerrainList, out var terrains))
        {
            terrains = new(1);
            context.Tags.Add(TerrainList, terrains);
        }
        terrains.Clear();

        foreach (var renderNode in RootRenderFeature.RenderObjects)
        {
            if (renderNode is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            if (!modelToTerrainMap.TryGetValue(renderModel, out var data))
            {
                continue;
            }

            if (!data.IsInitialized)
            {
                renderMesh.Enabled = false;
                continue;
            }

            // Update global buffer data
            data.ChunkBuffer!.SetData(context.CommandList, (ReadOnlySpan<ChunkData>)data.ChunkData.AsSpan(0, data.ChunkCount));
            data.SectorToChunkMapBuffer!.SetData(context.CommandList, (ReadOnlySpan<int>)data.SectorToChunkMap.AsSpan());

            // Update per frame data
            var terrainLogicalGroupKey = ((RootEffectRenderFeature)RootRenderFeature).CreateFrameLogicalGroup("Terrain");
            foreach (var frameLayout in ((RootEffectRenderFeature)RootRenderFeature).FrameLayouts)
            {
                var chunkSizeOffset = frameLayout.GetConstantBufferOffset(_chunkSizeOffset);
                if (chunkSizeOffset == -1)
                    continue;

                var resourceGroup = frameLayout.Entry.Resources;
                var mappedCB = resourceGroup.ConstantBuffer.Data;

                var perFrameTerrain = (PerFrameTerrain*)((byte*)mappedCB + chunkSizeOffset);
                perFrameTerrain->ChunkSize = (uint)data.TerrainData.Header.ChunkSize;
                perFrameTerrain->InvTerrainTextureSize = TerrainRuntimeData.InvRuntimeTextureSize;
                perFrameTerrain->InvTerrainSize = 1.0f / (data.TerrainData.Header.Size * data.UnitsPerTexel);
                perFrameTerrain->MaxHeight = data.TerrainData.Header.MaxHeight;
                perFrameTerrain->ChunksPerRow = (uint)data.ChunksPerRowLod0;
                perFrameTerrain->InvUnitsPerTexel = 1.0f / data.UnitsPerTexel;

                var logicalGroup = frameLayout.GetLogicalGroup(terrainLogicalGroupKey);
                if (logicalGroup.Hash == ObjectId.Empty)
                    continue;

                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 0, data.HeightmapTexture);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 1, data.NormalMapTexture);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 2, data.ChunkBuffer);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 3, data.SectorToChunkMapBuffer);
            }

            terrains.Add(data);
            break; // Currently only support single terrain
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage)
    {
        base.Draw(context, renderView, renderViewStage);

        if (Context.VisibilityGroup == null || !Context.VisibilityGroup.Tags.TryGetValue(ModelToTerrainMap, out var modelToTerrainMap))
            return;

        var frustum = new BoundingFrustum(ref renderView.ViewProjection);

        using var profilingScope = context.QueryManager.BeginProfile(Color4.Black, ProflingKeyCull);

        foreach (var renderNode in RootRenderFeature.RenderObjects)
        {
            // TODO: Check render stage index thing? It currently triggers in the debug renderer which it should not?
            // Or just do this properly with the override for the alternative Draw(...).
            if (renderNode is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            if (!modelToTerrainMap.TryGetValue(renderModel, out var data))
            {
                continue;
            }

            if (!data.IsInitialized)
            {
                renderMesh.Enabled = false;
                continue;
            }

            // Frustum cull chunks. Could use dispatcher but not seeing any difference in timings.
            var maxChunks = data.ChunksPerRowLod0 * data.ChunksPerRowLod0;
            renderMesh.InstanceCount = 0;
            var chunkInstanceData = ArrayPool<int>.Shared.Rent(maxChunks);
            for (var i = 0; i < data.ChunkCount; i++)
            {
                if (!VisibilityGroup.FrustumContainsBox(ref frustum, ref data.ChunkData[i].Bounds, renderView.VisiblityIgnoreDepthPlanes))
                    continue;

                chunkInstanceData[renderMesh.InstanceCount++] = i;
            }

            // Upload to GPU.
            data.ChunkInstanceData!.SetData(context.CommandList, (ReadOnlySpan<int>)chunkInstanceData.AsSpan(0, renderMesh.InstanceCount));

            ArrayPool<int>.Shared.Return(chunkInstanceData);

            // Update instancing material data            
            renderMesh.MaterialPass.Parameters.Set(MaterialTerrainDisplacementKeys.ChunkInstanceData, data.ChunkInstanceData);
        }
    }
}
