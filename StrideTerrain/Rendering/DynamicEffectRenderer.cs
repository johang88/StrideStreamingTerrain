using Stride.Core;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Shaders;

namespace StrideTerrain.Rendering;

public abstract class DynamicEffectRenderer
{
    public readonly ParameterCollection Parameters;
    public readonly DynamicEffectInstance Effect;
    public readonly MutablePipelineState PipelineState;

    private EffectBytecode? _previousBytecode;
    private bool _pipelineStateDirty = true;

    public DynamicEffectRenderer(IServiceRegistry services, GraphicsDevice graphicsDevice, string effectName)
    {
        Parameters = new();
        Effect = new(effectName, Parameters);
        Effect.Initialize(services);

        PipelineState = new(graphicsDevice);
        PipelineState.State.SetDefaults();
        PipelineState.State.PrimitiveType = PrimitiveType.TriangleList;
    }

    protected virtual void ConfigurePipelineState(CommandList commandList)
    {
        var renderTargets = commandList.RenderTargets;

        PipelineState.State.Output.RenderTargetCount = commandList.RenderTargetCount;
        PipelineState.State.Output.RenderTargetFormat0 = renderTargets.Length > 0 ? renderTargets[0].Format : PixelFormat.None;
        PipelineState.State.Output.RenderTargetFormat1 = renderTargets.Length > 1 ? renderTargets[1].Format : PixelFormat.None;
        PipelineState.State.Output.RenderTargetFormat2 = renderTargets.Length > 2 ? renderTargets[2].Format : PixelFormat.None;
        PipelineState.State.Output.RenderTargetFormat3 = renderTargets.Length > 3 ? renderTargets[3].Format : PixelFormat.None;
        PipelineState.State.Output.RenderTargetFormat4 = renderTargets.Length > 4 ? renderTargets[4].Format : PixelFormat.None;
        PipelineState.State.Output.RenderTargetFormat5 = renderTargets.Length > 5 ? renderTargets[5].Format : PixelFormat.None;
        PipelineState.State.Output.RenderTargetFormat6 = renderTargets.Length > 6 ? renderTargets[6].Format : PixelFormat.None;
        PipelineState.State.Output.RenderTargetFormat7 = renderTargets.Length > 7 ? renderTargets[7].Format : PixelFormat.None;
        PipelineState.State.Output.DepthStencilFormat = commandList.DepthStencilBuffer?.Format ?? PixelFormat.None;
        PipelineState.State.PrimitiveType = PrimitiveType.TriangleList;
    }

    public void PrepareDraw(RenderDrawContext context)
    {
        // Apply effect and pipeline state
        if (Effect.UpdateEffect(context.GraphicsDevice) || _pipelineStateDirty || _previousBytecode != Effect.Effect.Bytecode)
        {
            // The EffectInstance might have been updated from outside
            _previousBytecode = Effect.Effect.Bytecode;

            ConfigurePipelineState(context.CommandList);

            PipelineState.State.RootSignature = Effect.RootSignature;
            PipelineState.State.EffectBytecode = Effect.Effect.Bytecode;

            PipelineState.Update();
            _pipelineStateDirty = false;
        }

        context.CommandList.SetPipelineState(PipelineState.CurrentState);

        Effect.Apply(context.GraphicsContext);
    }

    public virtual void Dispose()
    {
        Effect.Dispose();
    }
}
