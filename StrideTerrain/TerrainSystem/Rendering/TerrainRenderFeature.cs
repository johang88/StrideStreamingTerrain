using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Storage;
using Stride.Rendering;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Effects.Material;
using System.Collections.Generic;

namespace StrideTerrain.TerrainSystem.Rendering;

public class TerrainRenderFeature : SubRenderFeature
{
    private static readonly ProfilingKey ProfilingKeyDraw = new("Terrain.Draw");
    private static readonly ProfilingKey ProflingKeyCull = new("Terrain.Cull");

    [DataMemberIgnore] public static readonly PropertyKey<Dictionary<RenderModel, TerrainRuntimeData>> ModelToTerrainMap = new("TerrainRenderFeature.ModelToTerrainMap", typeof(TerrainRenderFeature));
    [DataMemberIgnore] public static readonly PropertyKey<TerrainRuntimeData> Current = new("TerrainRenderFeature.Current", typeof(TerrainRenderFeature));

    [DataMember] public RenderStage? OpaqueRenderStage { get; set; }

    private ConstantBufferOffsetReference _chunkSizeOffset;

    private RenderMesh? _renderMesh;

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

        _renderMesh = null;

        if (Context.VisibilityGroup == null || !Context.VisibilityGroup.Tags.TryGetValue(ModelToTerrainMap, out var modelToTerrainMap))
        {
            context.Tags.Remove(Current);
            return;
        }

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

            _renderMesh = renderMesh;

            // Update global buffer data
            data.MeshManager!.UpdateBuffers(context.CommandList);

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
                perFrameTerrain->TerrainTextureSize = TerrainRuntimeData.RuntimeTextureSize;
                perFrameTerrain->InvTerrainSize = 1.0f / (data.TerrainData.Header.Size * data.UnitsPerTexel);

                perFrameTerrain->InvShadowMapSize = 0.0f;
                if (data.GpuTextureManager!.ShadowMap != null)
                {
                    float invUnitsPerTexel = 1.0f / data.UnitsPerTexel;
                    float invShadowMapsSize = invUnitsPerTexel * (1.0f / data.TerrainData.Header.Size);

                    perFrameTerrain->InvShadowMapSize = invShadowMapsSize;
                    perFrameTerrain->InvMaxHeight = 1.0f / data.TerrainData.Header.MaxHeight;
                }

                perFrameTerrain->MaxHeight = data.TerrainData.Header.MaxHeight;
                perFrameTerrain->ChunksPerRow = (uint)data.ChunksPerRowLod0;
                perFrameTerrain->InvUnitsPerTexel = 1.0f / data.UnitsPerTexel;
                perFrameTerrain->UnitsPerTexel = data.UnitsPerTexel;

                var logicalGroup = frameLayout.GetLogicalGroup(terrainLogicalGroupKey);
                if (logicalGroup.Hash == ObjectId.Empty)
                    continue;

                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 0, data.GpuTextureManager!.Heightmap.AtlasTexture);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 1, data.GpuTextureManager!.NormalMap.AtlasTexture);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 2, data.GpuTextureManager!.ControlMap.AtlasTexture);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 3, data.GpuTextureManager!.ShadowMap);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 4, data.MeshManager.ChunkBuffer);
                resourceGroup.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 5, data.MeshManager.SectorToChunkMapBuffer);
            }

            context.Tags.Set(Current, data);
            break; // Currently only support single terrain
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage)
    {
        base.Draw(context, renderView, renderViewStage);

        if (Context.VisibilityGroup == null || !Context.VisibilityGroup.Tags.TryGetValue(ModelToTerrainMap, out var modelToTerrainMap))
            return;

        if (renderView.Index != OpaqueRenderStage?.Index)
            return;

        if (_renderMesh == null)
            return;

        var renderModel = _renderMesh.RenderModel;
        if (renderModel == null)
            return;

        if (!modelToTerrainMap.TryGetValue(renderModel, out var data))
            return;

        if (!data.IsInitialized)
        {
            _renderMesh.Enabled = false;
            return;
        }

        using (var profilingScope = context.QueryManager.BeginProfile(Color4.Black, ProflingKeyCull))

        // Prepare and upload instancing data for the draw call.
        data.MeshManager!.PrepareDraw(context.CommandList, _renderMesh, renderView);
        _renderMesh.MaterialPass.Parameters.Set(MaterialTerrainDisplacementKeys.ChunkInstanceData, data.MeshManager.ChunkInstanceDataBuffer);
    }
}
