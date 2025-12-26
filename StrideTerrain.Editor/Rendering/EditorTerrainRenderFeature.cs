using Stride.Rendering;
using StrideTerrain.Editor.Effects;

namespace StrideTerrain.Editor.Rendering;

public class EditorTerrainRenderFeature : SubRenderFeature
{
    public TerrainMeshManager? MeshManager { get; set; }
    private RenderMesh? _renderMesh;

    public override void Extract()
    {
        if (Context.VisibilityGroup == null || MeshManager == null)
            return;

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            if (objectNode.RenderObject is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            // TODO: have to check if terrain!

            renderMesh.Enabled = true;

            break;
        }
    }

    public override unsafe void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        _renderMesh = null;

        if (Context.VisibilityGroup == null || MeshManager == null)
        {
            return;
        }

        foreach (var renderNode in RootRenderFeature.RenderObjects)
        {
            if (renderNode is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            // TODO: have to check if terrain!

            _renderMesh = renderMesh;

            MeshManager!.UpdateBuffers(context.CommandList);

            break;
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage)
    {
        base.Draw(context, renderView, renderViewStage);

        if (Context.VisibilityGroup == null || MeshManager == null)
            return;

        if (_renderMesh == null)
            return;

        var renderModel = _renderMesh.RenderModel;
        if (renderModel == null)
            return;

        // Prepare and upload instancing data for the draw call.
        MeshManager.PrepareDraw(context.CommandList, _renderMesh, renderView);
        _renderMesh.MaterialPass.Parameters.Set(EditorTerrainDisplacementKeys.ChunkInstanceData, MeshManager.ChunkInstanceDataBuffer);
        _renderMesh.MaterialPass.Parameters.Set(EditorTerrainDisplacementKeys.ChunkBuffer, MeshManager.ChunkBuffer);
        _renderMesh.MaterialPass.Parameters.Set(EditorTerrainDisplacementKeys.SectorToChunkMapBuffer, MeshManager.SectorToChunkMapBuffer);
        _renderMesh.MaterialPass.Parameters.Set(EditorTerrainDisplacementKeys.ChunkSize, (uint)TerrainMeshManager.ChunkSize);
    }
}
