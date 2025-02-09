using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using Stride.Rendering.Images;
using StrideTerrain.Rendering.Effects;
using System;

namespace StrideTerrain.Rendering;

internal class RadiancePrefilteringGGXV2 : DrawEffect
{
    private int samplingsCount;

    private readonly ComputeEffectShader computeShader;

    /// <summary>
    /// Gets or sets the boolean indicating if the highest level of mipmaps should be let as-is or pre-filtered.
    /// </summary>
    public bool DoNotFilterHighestLevel { get; set; }

    /// <summary>
    /// Gets or sets the input radiance map to pre-filter.
    /// </summary>
    public Texture RadianceMap { get; set; }

    /// <summary>
    /// Gets or sets the texture to use to store the result of the pre-filtering.
    /// </summary>
    public Texture PrefilteredRadiance { get; set; }

    /// <summary>
    /// Gets or sets the number of pre-filtered mipmap to generate.
    /// </summary>
    public int MipmapGenerationCount { get; set; }

    /// <summary>
    /// Create a new instance of the class.
    /// </summary>
    /// <param name="context">the context</param>
    public RadiancePrefilteringGGXV2(RenderContext context)
        : base(context, "RadiancePrefilteringGGXV2")
    {
        computeShader = new ComputeEffectShader(context) { ShaderSourceName = "RadiancePrefilteringGGXEffectV2" };
        DoNotFilterHighestLevel = true;
        samplingsCount = 1024;
    }

    /// <summary>
    /// Gets or sets the number of sampling used during the importance sampling
    /// </summary>
    /// <remarks>Should be a power of 2 and maximum value is 8192</remarks>
    public int SamplingsCount
    {
        get { return samplingsCount; }
        set
        {
            if (value > 8192)
                throw new ArgumentOutOfRangeException("value");

            if (!MathUtil.IsPow2(value))
                throw new ArgumentException("The provided value should be a power of 2");

            samplingsCount = Math.Max(1, value);
        }
    }

    protected override void DrawCore(RenderDrawContext context)
    {
        var output = PrefilteredRadiance;
        if (output == null || (output.ViewDimension != TextureDimension.Texture2D && output.ViewDimension != TextureDimension.TextureCube) || output.ArraySize != 6)
            throw new NotSupportedException("Only array of 2D textures are currently supported as output");

        if (!output.IsUnorderedAccess || output.IsRenderTarget)
            throw new NotSupportedException("Only non-rendertarget unordered access textures are supported as output");

        var input = RadianceMap;
        if (input == null || input.Dimension != TextureDimension.TextureCube)
            throw new NotSupportedException("Only cubemaps are currently supported as input");

        var roughness = 0f;
        var faceCount = output.ArraySize;
        var levelSize = new Int2(output.Width, output.Height);
        var mipCount = MipmapGenerationCount == 0 ? output.MipLevels : MipmapGenerationCount;

        for (int l = 0; l < mipCount; l++)
        {
            if (l == 0 && DoNotFilterHighestLevel && input.Width >= output.Width)
            {
                var inputLevel = MathUtil.Log2(input.Width / output.Width);
                for (int f = 0; f < 6; f++)
                {
                    var inputSubresource = inputLevel + f * input.MipLevels;
                    var outputSubresource = 0 + f * output.MipLevels;
                    context.CommandList.CopyRegion(input, inputSubresource, null, output, outputSubresource);
                }
            }
            else
            {
                var outputView = output.ToTextureView(ViewType.MipBand, 0, l);

                var blockSize = 8;
                var threadOffLoad = 16;

                computeShader.ThreadGroupCounts = new Int3((levelSize.X + blockSize - 1) / blockSize, (levelSize.Y + blockSize - 1) / blockSize, faceCount);
                computeShader.ThreadNumbers = new Int3(blockSize, blockSize, threadOffLoad);
                computeShader.Parameters.Set(RadiancePrefilteringGGXShaderV2Keys.Roughness, roughness);
                computeShader.Parameters.Set(RadiancePrefilteringGGXShaderV2Keys.MipmapCount, input.MipLevels - 1);
                computeShader.Parameters.Set(RadiancePrefilteringGGXShaderV2Keys.RadianceMap, input);
                computeShader.Parameters.Set(RadiancePrefilteringGGXShaderV2Keys.RadianceMapSize, (uint)input.Width);
                computeShader.Parameters.Set(RadiancePrefilteringGGXShaderV2Keys.InvRadianceMapSize, 1.0f / input.Width);
                computeShader.Parameters.Set(RadiancePrefilteringGGXShaderV2Keys.FilteredRadianceMapSize, (uint)levelSize.X);
                computeShader.Parameters.Set(RadiancePrefilteringGGXShaderV2Keys.FilteredRadiance, outputView);
                computeShader.Parameters.Set(RadiancePrefilteringGGXV2Params.NbOfSamplings, (uint)SamplingsCount);
                computeShader.Parameters.Set(RadiancePrefilteringGGXV2Params.BlockSize, (uint)blockSize);
                computeShader.Parameters.Set(RadiancePrefilteringGGXV2Params.ThreadOffload, (uint)threadOffLoad);
                computeShader.Draw(context);

                outputView.Dispose();
            }

            if (mipCount > 1)
            {
                roughness += 1f / (mipCount - 1);
                levelSize /= 2;
            }
        }
    }
}
