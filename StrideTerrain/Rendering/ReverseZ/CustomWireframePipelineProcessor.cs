using Stride.Graphics;
using Stride.Rendering;

namespace StrideTerrain.Rendering.ReverseZ;

public class CustomWireframePipelineProcessor : PipelineProcessor
{
    public RenderStage? RenderStage { get; set; }

    public override void Process(RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        if (renderNode.RenderStage == RenderStage)
        {
            pipelineState.RasterizerState = RasterizerStates.Wireframe;
            pipelineState.DepthStencilState.DepthBufferFunction = CompareFunction.GreaterEqual;
        }
    }
}
