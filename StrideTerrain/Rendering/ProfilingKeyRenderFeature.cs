using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Rendering;
using System.Collections.Generic;

namespace StrideTerrain.Rendering;

/// <summary>
/// Sets a profiling key on render mesh.
/// </summary>
public class ProfilingKeyRenderFeature : SubRenderFeature
{
    [DataMemberIgnore]
    public static readonly PropertyKey<Dictionary<RenderModel, ProfilingKey?>> ModelToProfilingKeyMap = new("ProfilingKeyRenderFeature.ModelToProfilingKeyMap", typeof(ProfilingKeyRenderFeature));

    public override void Extract()
    {
        base.Extract();

        if ((Context.VisibilityGroup == null) || (!Context.VisibilityGroup.Tags.TryGetValue(ModelToProfilingKeyMap, out var modelToProfilingKeyMap)))
            return;

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            if (objectNode.RenderObject is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            if (!modelToProfilingKeyMap.TryGetValue(renderModel, out var profilingKey))
            {
                continue;
            }

            renderMesh.ProfilingKey = profilingKey;
        }
    }
}
