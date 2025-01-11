using Stride.Core;
using Stride.Rendering;
using System.Collections.Generic;
namespace StrideTerrain.TerrainSystem;

public class TerrainRenderFeature : SubRenderFeature
{
    [DataMemberIgnore]
    public static readonly PropertyKey<Dictionary<RenderModel, TerrainRuntimeData>> ModelToTerrainMap = new("TerrainRenderFeature.ModelToTerrainMap", typeof(TerrainRenderFeature));

    public override void Extract()
    {
        if ((Context.VisibilityGroup == null) || (!Context.VisibilityGroup.Tags.TryGetValue(ModelToTerrainMap, out var modelToTerrainMap)))
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

            if (terrainRenderData.InstanceCount == 0)
            {
                renderMesh.Enabled = false;
                continue;
            }

            renderMesh.Enabled = true;

            renderMesh.InstanceCount = terrainRenderData.InstanceCount;
        }
    }
}
