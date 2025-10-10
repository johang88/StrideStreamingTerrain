using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Rendering;

namespace StrideTerrain.Rendering;

[DataContract("CustomSortMode")]
public class CustomSortMode : StateChangeSortMode
{
    public override unsafe void GenerateSortKey(RenderView renderView, RenderViewStage renderViewStage, SortKey* sortKeys)
    {
        Matrix.Invert(ref renderView.View, out var viewInverse);
        var plane = new Plane(viewInverse.Forward, Vector3.Dot(viewInverse.TranslationVector, viewInverse.Forward)); // TODO: Point-normal-constructor seems wrong. Check.

        var renderNodes = renderViewStage.RenderNodes;

        int distanceShift = 32 - distancePrecision;
        int stateShift = 32 - statePrecision;

        for (int i = 0; i < renderNodes.Count; ++i)
        {
            var renderNode = renderNodes[i];

            var renderObject = renderNode.RenderObject;
            var distance = CollisionHelper.DistancePlanePoint(ref plane, ref renderObject.BoundingBox.Center);
            var distanceI = ComputeDistance(distance);

            // Customize some sorting keys as we don't have proper distances to some big meshes
            uint stateSortKey = renderObject.RenderGroup switch
            {
                RenderGroups.Terrain => 0,
                RenderGroups.Impostors => uint.MaxValue, // Last
                _ => renderObject.StateSortKey
            };

            // Compute sort key
            sortKeys[i] = new SortKey { Value = ((ulong)renderNode.RootRenderFeature.SortKey << 56) | ((ulong)(distanceI >> distanceShift) << distancePosition) | ((ulong)(stateSortKey >> stateShift) << statePosition), Index = i, StableIndex = renderObject.Index };
        }
    }
}
