using Stride.Rendering;
using Stride.Rendering.Images;

namespace StrideTerrain.TerrainSystem;

public class CustomImageEffect : ImageEffectShader
{
    public CustomImageEffect(string effectName) : base(effectName)
    {
        EnableSetRenderTargets = false;
    }

    protected override void DrawCore(RenderDrawContext context)
    {
        SetDepthOutput(context.CommandList.DepthStencilBuffer, context.CommandList.RenderTarget); 

        base.DrawCore(context);
    }
}
