using Stride.Rendering;
using Stride.Graphics;
using Stride.Core;
using System.Collections.Generic;
using System.Numerics;
using Stride.Core.Diagnostics;
using Stride.Core.Threading;

namespace StrideTerrain.Vegetation;

public class GrassRenderFeature : SubRenderFeature
{
    public struct GrassData
    {
        public Buffer? IndirectBuffer;
        public Buffer? WorldBuffer;
        public Buffer? WorldInverseBuffer;
    }

    [DataMemberIgnore]
    public static readonly PropertyKey<Dictionary<RenderModel, RenderGrass>> ModelToGrassMap = new("GrassRenderFeature.ModelToGrassMap", typeof(InstancingRenderFeature));

    private StaticObjectPropertyKey<GrassData> _renderObjectGrassDataInfoKey;
    private StaticObjectPropertyKey<RenderEffect> _renderEffectKey;
    private LogicalGroupReference _instancingGroupKey;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _renderObjectGrassDataInfoKey = RootRenderFeature.RenderData.CreateStaticObjectKey<GrassData>();
        _renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;
        _instancingGroupKey = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawLogicalGroup("Instancing");
    }

    public override void Extract()
    {
        base.Extract();

        if ((Context.VisibilityGroup == null) || (!Context.VisibilityGroup.Tags.TryGetValue(ModelToGrassMap, out var modelToGrassMap)))
            return;

        var renderObjectGrassData = RootRenderFeature.RenderData.GetData(_renderObjectGrassDataInfoKey);

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            if (objectNode.RenderObject is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            if (!modelToGrassMap.TryGetValue(renderModel, out var renderGrass) || renderGrass.IndirectBuffer == null || renderGrass.WorldBuffer == null || renderGrass.WorldInverseBuffer == null)
            {
                continue;
            }

            ref var grassData = ref renderObjectGrassData[renderMesh.StaticObjectNode];

            grassData.IndirectBuffer = renderGrass.IndirectBuffer;
            grassData.WorldBuffer = renderGrass.WorldBuffer;
            grassData.WorldInverseBuffer = renderGrass.WorldInverseBuffer;

            renderMesh.IndirectBuffer = grassData.IndirectBuffer;
        }
    }

    public unsafe override void Prepare(RenderDrawContext context)
    {
        var renderObjectGrassData = RootRenderFeature.RenderData.GetData(_renderObjectGrassDataInfoKey);

        // Assign buffers to render node
        foreach (var renderNode in ((RootEffectRenderFeature)RootRenderFeature).RenderNodes)
        {
            var perDrawLayout = renderNode.RenderEffect.Reflection?.PerDrawLayout;
            if (perDrawLayout == null)
                continue;

            var group = perDrawLayout.GetLogicalGroup(_instancingGroupKey);
            if (group.DescriptorEntryStart == -1)
                continue;

            if (renderNode.RenderObject is not RenderMesh renderMesh)
                continue;

            ref var instancingData = ref renderObjectGrassData[renderMesh.StaticObjectNode];

            if (instancingData.IndirectBuffer != null)
            {
                renderNode.Resources.DescriptorSet.SetShaderResourceView(group.DescriptorEntryStart, instancingData.WorldBuffer);
                renderNode.Resources.DescriptorSet.SetShaderResourceView(group.DescriptorEntryStart + 1, instancingData.WorldInverseBuffer);
            }
        }
    }

    public override void PrepareEffectPermutations(RenderDrawContext context)
    {
        var renderObjectInstancingData = RootRenderFeature.RenderData.GetData(_renderObjectGrassDataInfoKey);

        var renderEffects = RootRenderFeature.RenderData.GetData(_renderEffectKey);
        int effectSlotCount = ((RootEffectRenderFeature)RootRenderFeature).EffectPermutationSlotCount;

        Dispatcher.ForEach(RootRenderFeature.RenderObjects, renderObject =>
        {
            var renderMesh = (RenderMesh)renderObject;

            var staticObjectNode = renderMesh.StaticObjectNode;
            var instancingData = renderObjectInstancingData[staticObjectNode];

            for (int i = 0; i < effectSlotCount; i++)
            {
                var staticEffectObjectNode = staticObjectNode * effectSlotCount + i;
                var renderEffect = renderEffects[staticEffectObjectNode];

                // Skip effects not used during this frame
                if (renderEffect == null || !renderEffect.IsUsedDuringThisFrame(RenderSystem))
                    continue;

                if (instancingData.IndirectBuffer != null)
                {
                    renderEffect.EffectValidator.ValidateParameter(StrideEffectBaseKeys.ModelTransformUsage, 0);
                    renderEffect.EffectValidator.ValidateParameter(StrideEffectBaseKeys.HasInstancing, true);
                }
            }
        });
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        base.Draw(context, renderView, renderViewStage, startIndex, endIndex);

        var renderObjectGrassData = RootRenderFeature.RenderData.GetData(_renderObjectGrassDataInfoKey);

        for (int index = startIndex; index < endIndex; index++)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = RootRenderFeature.GetRenderNode(renderNodeReference);

            if (renderNode.RenderObject is not RenderMesh renderMesh)
                continue;

            var renderModel = renderMesh.RenderModel;
            if (renderModel == null)
                continue;

            ref var grassData = ref renderObjectGrassData[renderMesh.StaticObjectNode];

            if (grassData.IndirectBuffer == null)
                continue;
        }
    }
}

struct DrawArgs
{
    public uint IndexCountPerInstance;
    public uint InstanceCount;
    public uint StartIndexLocation;
    public int BaseVertexLocation;
    public uint StartInstanceLocation;
};
