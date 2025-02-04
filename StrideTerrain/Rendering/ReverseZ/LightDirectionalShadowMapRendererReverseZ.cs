using Stride.Core.Mathematics;
using Stride.Rendering;
using Stride.Rendering.Shadows;
namespace StrideTerrain.Rendering.ReverseZ;

public class LightDirectionalShadowMapRendererReverseZ : LightDirectionalShadowMapRenderer
{
    public override void Collect(RenderContext context, RenderView sourceView, LightShadowMapTexture lightShadowMap)
    {
        Matrix reverseZMatrix =
            new(1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, -1.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 1.0f);

        Matrix reverseZMatrixInverse = Matrix.Invert(reverseZMatrix);

        var orgProjection = sourceView.Projection;
        var orgViewProjection = sourceView.ViewProjection;

        sourceView.Projection = Matrix.Multiply(sourceView.Projection, reverseZMatrix);
        Matrix.Multiply(ref sourceView.View, ref sourceView.Projection, out sourceView.ViewProjection);

        base.Collect(context, sourceView, lightShadowMap);

        sourceView.Projection = orgProjection;
        sourceView.ViewProjection = orgViewProjection;
    }
}
