using Stride.Rendering;
using Stride.Graphics;
using Stride.Core;
using System.Collections.Generic;
using Stride.Core.Threading;
using Stride.Rendering.ComputeEffect;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using System.Runtime.CompilerServices;
using StrideTerrain.Rendering;

namespace StrideTerrain.Vegetation;

public class GrassRenderFeature : SubRenderFeature
{
    public struct GrassData
    {
        public Buffer? IndirectBuffer;
        public Buffer? InstancesBuffer;
        public Buffer? CulledWorldBuffer;
        public Buffer? CulledWorldInverseBuffer;
        public int Size;
        public float BoundingRadius;
    }

    [DataMemberIgnore]
    public static readonly PropertyKey<Dictionary<RenderModel, RenderGrass>> ModelToGrassMap = new("GrassRenderFeature.ModelToGrassMap", typeof(InstancingRenderFeature));

    private StaticObjectPropertyKey<GrassData> _renderObjectGrassDataInfoKey;
    private StaticObjectPropertyKey<RenderEffect> _renderEffectKey;
    private LogicalGroupReference _instancingGroupKey;

    private IndirectComputeEffectShader? _cullGrassShader;
    private ComputeEffectShader? _setupIndirectDispatchShader;

    private Buffer _indirectDispatchTempBuffer = null!;
    private Buffer _indirectDispatchBuffer = null!;
    private Vector4[] _frustumPlanes = new Vector4[6];

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _renderObjectGrassDataInfoKey = RootRenderFeature.RenderData.CreateStaticObjectKey<GrassData>();
        _renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;
        _instancingGroupKey = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawLogicalGroup("Instancing");

        _cullGrassShader ??= new(Context)
        {
            ShaderSourceName = "GrassCullInstances"
        };
        _cullGrassShader.DisposeBy(this);

        _setupIndirectDispatchShader ??= new (Context)
        {
            ShaderSourceName = "SetupIndirectDispatchArgs"
        };
        _setupIndirectDispatchShader.DisposeBy(this);

        _indirectDispatchTempBuffer = Buffer.New(Context.GraphicsDevice, Marshal.SizeOf<DispatchArgs>(), BufferFlags.RawBuffer | BufferFlags.UnorderedAccess | BufferFlags.ShaderResource);
        _indirectDispatchBuffer = Buffer.New(Context.GraphicsDevice, Marshal.SizeOf<DispatchArgs>(), BufferFlags.ArgumentBuffer);
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

            if (!modelToGrassMap.TryGetValue(renderModel, out var renderGrass)
                || renderGrass.IndirectBuffer == null || renderGrass.InstancesBuffer == null
                || renderGrass.CulledWorldBuffer == null || renderGrass.CulledWorldInverseBuffer == null)
            {
                continue;
            }

            ref var grassData = ref renderObjectGrassData[renderMesh.StaticObjectNode];

            grassData.IndirectBuffer = renderGrass.IndirectBuffer;
            grassData.InstancesBuffer = renderGrass.InstancesBuffer;
            grassData.CulledWorldBuffer = renderGrass.CulledWorldBuffer;
            grassData.CulledWorldInverseBuffer = renderGrass.CulledWorldInverseBuffer;
            grassData.Size = renderGrass.Size;
            grassData.BoundingRadius = renderGrass.BoundingRadius;

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
                renderNode.Resources.DescriptorSet.SetShaderResourceView(group.DescriptorEntryStart, instancingData.CulledWorldBuffer);
                renderNode.Resources.DescriptorSet.SetShaderResourceView(group.DescriptorEntryStart + 1, instancingData.CulledWorldInverseBuffer);
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

            if (grassData.IndirectBuffer == null || grassData.CulledWorldBuffer == null || grassData.CulledWorldInverseBuffer == null
                || grassData.InstancesBuffer == null || _cullGrassShader == null || _setupIndirectDispatchShader == null)
                continue;

            // Prepare dispatch indirect buffer
            context.CommandList.CopyCount(grassData.InstancesBuffer, _indirectDispatchTempBuffer, 0);

            // We have to use a temporary buffer as DX11 does not allow us to bind the indirect args buffer directly
            // at least as far as I have been able to figure out ...
            _setupIndirectDispatchShader.Parameters.Set(SetupIndirectDispatchArgsKeys.IndirectArgsBuffer, _indirectDispatchTempBuffer);
            _setupIndirectDispatchShader.Parameters.Set(SetupIndirectDispatchArgsKeys.ThreadsPerGroup, 64u);

            _setupIndirectDispatchShader.ThreadGroupCounts = new(1, 1, 1);
            _setupIndirectDispatchShader.ThreadNumbers = new(1, 1, 1);

            _setupIndirectDispatchShader.Draw(context, "Grass.SetupIndirectDispatch");

            context.CommandList.Copy(_indirectDispatchTempBuffer, _indirectDispatchBuffer);

            // Cull instances
            grassData.CulledWorldBuffer.InitialCounterOffset = 0;
            grassData.CulledWorldInverseBuffer.InitialCounterOffset = 0;

            _frustumPlanes[0] = new(renderView.Frustum.LeftPlane.Normal, renderView.Frustum.LeftPlane.D);
            _frustumPlanes[1] = new(renderView.Frustum.RightPlane.Normal, renderView.Frustum.RightPlane.D);
            _frustumPlanes[2] = new(renderView.Frustum.TopPlane.Normal, renderView.Frustum.TopPlane.D);
            _frustumPlanes[3] = new(renderView.Frustum.BottomPlane.Normal, renderView.Frustum.BottomPlane.D);
            _frustumPlanes[4] = new(renderView.Frustum.NearPlane.Normal, renderView.Frustum.NearPlane.D);
            _frustumPlanes[5] = new(renderView.Frustum.FarPlane.Normal, renderView.Frustum.FarPlane.D);

            _cullGrassShader.Parameters.Set(GrassCullInstancesKeys.BoundingRadius, grassData.BoundingRadius);
            _cullGrassShader.Parameters.Set(GrassCullInstancesKeys.FrustumPlanes, _frustumPlanes);

            _cullGrassShader.Parameters.Set(GrassCullInstancesKeys.Instances, grassData.InstancesBuffer);
            _cullGrassShader.Parameters.Set(GrassCullInstancesKeys.OutputWorld, grassData.CulledWorldBuffer);
            _cullGrassShader.Parameters.Set(GrassCullInstancesKeys.OutputWorldInverse, grassData.CulledWorldInverseBuffer);

            _cullGrassShader.IndirectBuffer = _indirectDispatchBuffer;
            _cullGrassShader.ThreadNumbers = new(64, 1, 1);
            _cullGrassShader.Draw(context, "Grass.Cull");

            // Copy culled count to indirect draw buffer
            context.CommandList.CopyCount(grassData.CulledWorldBuffer, grassData.IndirectBuffer, 4);
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
