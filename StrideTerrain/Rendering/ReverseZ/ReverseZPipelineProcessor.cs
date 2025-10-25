using Stride.Graphics;
using Stride.Rendering;
using System.Collections.Generic;

namespace StrideTerrain.Rendering.ReverseZ;

public class ReverseZPipelineProcessor : PipelineProcessor
{
    public List<RenderStage> ExcludedRenderStages = [];
    public required RenderStage Opaque { get; set; }

    public override void Process(RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        if (ExcludedRenderStages.Contains(renderNode.RenderStage))
            return;

        if (renderNode.RenderStage == Opaque)
            pipelineState.DepthStencilState.DepthBufferFunction = CompareFunction.Equal;
        else
            pipelineState.DepthStencilState.DepthBufferFunction = CompareFunction.GreaterEqual;
    }
}
