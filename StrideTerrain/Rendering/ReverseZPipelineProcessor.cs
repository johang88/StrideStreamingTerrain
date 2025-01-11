using Stride.Graphics;
using Stride.Rendering;
using System.Collections.Generic;

namespace StrideTerrain.Rendering;

public class ReverseZPipelineProcessor : PipelineProcessor
{
    public List<RenderStage> ExcludedRenderStages = [];

    public override void Process(RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        if (ExcludedRenderStages.Contains(renderNode.RenderStage))
            return;

        pipelineState.DepthStencilState.DepthBufferFunction = CompareFunction.Greater;
    }
}
