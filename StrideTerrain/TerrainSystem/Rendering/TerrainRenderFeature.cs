using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Rendering;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Effects.Material;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace StrideTerrain.TerrainSystem.Rendering;

public class TerrainRenderFeature : SubRenderFeature
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Terrain.Draw");
    private static readonly ProfilingKey ProflingKeyCull = new("Terrain.Cull");

    [DataMemberIgnore]
    public static readonly PropertyKey<Dictionary<RenderModel, TerrainRuntimeData>> ModelToTerrainMap = new("TerrainRenderFeature.ModelToTerrainMap", typeof(TerrainRenderFeature));
    public static readonly PropertyKey<List<TerrainRuntimeData>> TerrainList = new("TerrainRenderFeature.Terrains", typeof(TerrainRenderFeature));

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
        }
    }

    public override void Prepare(RenderDrawContext context)
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

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            if (objectNode.RenderObject is not RenderMesh renderMesh)
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

            terrains.Add(data);
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage)
    {
        base.Draw(context, renderView, renderViewStage);

        if (Context.VisibilityGroup == null || !Context.VisibilityGroup.Tags.TryGetValue(ModelToTerrainMap, out var modelToTerrainMap))
            return;

        var frustum = new BoundingFrustum(ref renderView.ViewProjection);

        using var profilingScope = context.QueryManager.BeginProfile(Color4.Black, ProflingKeyCull);

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            // TODO: Check render stage index thing? It currently triggers in the debug renderer which it should not?
            // Or just do this properly with the override for the alternative Draw(...).
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            if (objectNode.RenderObject is not RenderMesh renderMesh)
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

            var terrainSize = data.TerrainData.Header.Size;
            var unitsPerTexel = data.UnitsPerTexel;
            var chunkSize = data.TerrainData.Header.ChunkSize;
            var invTerrainSize = 1.0f / (terrainSize * unitsPerTexel);

            data.ChunksPerRowLod0 = terrainSize / chunkSize;
            var maxChunks = data.ChunksPerRowLod0 * data.ChunksPerRowLod0;

            // Frustum cull chunks. Could use dispatcher but not seeing any difference in timings.
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

            // Update instancing and material data
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.ChunkSize, (uint)chunkSize);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.InvTerrainTextureSize, TerrainRuntimeData.InvRuntimeTextureSize);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.InvTerrainSize, invTerrainSize);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.Heightmap, data.HeightmapTexture);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.MaxHeight, data.TerrainData.Header.MaxHeight);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.ChunkBuffer, data.ChunkBuffer);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.SectorToChunkMapBuffer, data.SectorToChunkMapBuffer);
            renderMesh.MaterialPass.Parameters.Set(MaterialTerrainDisplacementKeys.ChunkInstanceData, data.ChunkInstanceData);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.ChunksPerRow, (uint)data.ChunksPerRowLod0);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.TerrainNormalMap, data.NormalMapTexture);
            renderMesh.MaterialPass.Parameters.Set(TerrainDataKeys.InvUnitsPerTexel, 1.0f / data.UnitsPerTexel);
        }
    }
}
