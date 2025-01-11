using Stride.Core.Mathematics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace StrideTerrain.Rendering;

public class ReverseZRenderer : SceneRendererBase
{
    public ISceneRenderer? Child { get; set; }

    protected override void CollectCore(RenderContext context)
    {
        base.CollectCore(context);

        Child?.Collect(context);

        Matrix reverseZMatrix =
            new(1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, -1.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 1.0f);

        context.RenderView.Projection = Matrix.Multiply(context.RenderView.Projection, reverseZMatrix);
        Matrix.Multiply(ref context.RenderView.View, ref context.RenderView.Projection, out context.RenderView.ViewProjection);
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        Child?.Draw(drawContext);
    }
}
