using Stride.Core;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;

namespace StrideTerrain.Weather;

public class LookupTextureEffect : ComponentBase
{
    public Texture Texture { get; }
    public ComputeEffectShader Effect { get; }

    public LookupTextureEffect(WeatherRenderFeature renderFeature, RenderContext context, string effectName, int width, int height, int depth, PixelFormat pixelFormat)
    {
        if (depth == 1)
        {
            Texture = Texture.New2D(context.GraphicsDevice, width, height, pixelFormat, TextureFlags.UnorderedAccess | TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        }
        else
        {
            Texture = Texture.New3D(context.GraphicsDevice, width, height, depth, pixelFormat, TextureFlags.UnorderedAccess | TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        }
        Texture.DisposeBy(this);

        Effect = new ComputeEffectShader(context) { ShaderSourceName = effectName };
        Effect.DisposeBy(this);

        this.DisposeBy(renderFeature);
    }
}
