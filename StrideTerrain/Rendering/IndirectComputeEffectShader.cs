using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using Stride.Shaders;

namespace StrideTerrain.Rendering;
public class IndirectComputeEffectShader : DrawEffect
{
    private MutablePipelineState pipelineState;
    private bool pipelineStateDirty = true;
    private EffectBytecode? previousBytecode;

    public IndirectComputeEffectShader(RenderContext context)
        : base(context, null)
    {
        pipelineState = new MutablePipelineState(context.GraphicsDevice);

        // Setup the effect compiler
        EffectInstance = new DynamicEffectInstance("ComputeEffectShader", Parameters);
        EffectInstance.Initialize(context.Services);

        // We give ComputeEffectShader a higher priority, since they are usually executed serially and blocking
        EffectInstance.EffectCompilerParameters.TaskPriority = -1;

        ThreadNumbers = new Int3(1);

        SetDefaultParameters();
    }

    /// <summary>
    /// The current effect instance.
    /// </summary>
    public DynamicEffectInstance EffectInstance { get; private set; }

    public Buffer? IndirectBuffer { get; set; }

    /// <summary>
    /// Gets or sets the number of threads desired by thread group.
    /// </summary>
    public Int3 ThreadNumbers { get; set; }

    /// <summary>
    /// Gets or sets the name of the input compute shader file (.sdsl)
    /// </summary>
    public string? ShaderSourceName { get; set; }

    /// <summary>
    /// Sets the default parameters (called at constructor time and if <see cref="DrawEffect.Reset"/> is called)
    /// </summary>
    protected override void SetDefaultParameters()
    {
    }

    protected override void PreDrawCore(RenderDrawContext context)
    {
        base.PreDrawCore(context);

        // Default handler for parameters
        UpdateParameters();
    }

    /// <summary>
    /// Updates the effect <see cref="DrawEffect.Parameters" /> from properties defined in this instance. See remarks.
    /// </summary>
    protected virtual void UpdateParameters()
    {
    }

    protected override void DrawCore(RenderDrawContext context)
    {
        if (string.IsNullOrEmpty(ShaderSourceName) || IndirectBuffer == null)
            return;

        Parameters.Set(ComputeEffectShaderKeys.ThreadNumbers, ThreadNumbers);
        Parameters.Set(ComputeEffectShaderKeys.ComputeShaderName, ShaderSourceName);

        if (EffectInstance.UpdateEffect(GraphicsDevice) || pipelineStateDirty || previousBytecode != EffectInstance.Effect.Bytecode)
        {
            previousBytecode = EffectInstance.Effect.Bytecode;

            pipelineState.State.SetDefaults();
            pipelineState.State.RootSignature = EffectInstance.RootSignature;
            pipelineState.State.EffectBytecode = EffectInstance.Effect.Bytecode;
            pipelineState.Update();
            pipelineStateDirty = false;
        }

        context.CommandList.SetPipelineState(pipelineState.CurrentState);

        EffectInstance.Apply(context.GraphicsContext);

        context.CommandList.Dispatch(IndirectBuffer, 0);
    }
}


struct DispatchArgs
{
    public uint ThreadGroupCountX;
    public uint ThreadGroupCountY;
    public uint ThreadGroupCountZ;
    public uint Padding;
};
