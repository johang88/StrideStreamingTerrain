using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Rendering;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Effects.Material;

namespace StrideTerrain.TerrainSystem.Rendering;

public class DiffuseRoughnessAtlasRenderer : DynamicEffectRenderer
{
    public Texture? MaterialDiffuseRoughnessArray { get; set; }

    public DiffuseRoughnessAtlasRenderer(IServiceRegistry services, GraphicsDevice graphicsDevice)
        : base(services, graphicsDevice, "TerrainMaterialDiffuseTileRenderer")
    {
    }

    protected override void ConfigurePipelineState(CommandList commandList)
    {
        base.ConfigurePipelineState(commandList);

        PipelineState.State.DepthStencilState.DepthBufferEnable = false;
        PipelineState.State.RasterizerState.CullMode = CullMode.None;
    }

    public void Draw(RenderDrawContext context, int chunkIndex, int textureIndex, TerrainRuntimeData data, StreamingTextureAtlas streamingTextureAtlas)
    {
        if (MaterialDiffuseRoughnessArray == null)
            return;

        var commandList = context.CommandList;

        using var _ = context.PushRenderTargetsAndRestore();

        var (worldMinX, worldMaxX, worldMinZ, worldMaxZ) = data.TerrainData.GetChunkWorldBounds(chunkIndex);

        var (tx, ty) = streamingTextureAtlas.GetCoordinates(textureIndex);

        var uvMin = new Vector2(tx, ty) / streamingTextureAtlas.Size;
        var uvMax = uvMin + new Vector2(streamingTextureAtlas.ChunkTextureSize, streamingTextureAtlas.ChunkTextureSize) / streamingTextureAtlas.Size;

        (uvMin.Y, uvMax.Y) = (uvMax.Y, uvMin.Y);

        // Render to staging
        commandList.SetRenderTarget(null, streamingTextureAtlas.StagingTexutre);
        commandList.SetViewport(new Viewport(0, 0, streamingTextureAtlas.StagingTexutre.Width, streamingTextureAtlas.StagingTexutre.Height));

        Parameters.Set(TerrainMaterialDiffuseTileRendererKeys.UvMin, uvMin);
        Parameters.Set(TerrainMaterialDiffuseTileRendererKeys.UvMax, uvMax);

        Parameters.Set(TerrainMaterialDiffuseTileRendererKeys.WorldBoundsMin, new Vector2(worldMinX, worldMinZ));
        Parameters.Set(TerrainMaterialDiffuseTileRendererKeys.WorldBoundsMax, new Vector2(worldMaxX, worldMaxZ));

        Parameters.Set(TerrainMaterialDiffuseTileRendererKeys.AtlasSize, streamingTextureAtlas.Size);

        Parameters.Set(TerrainMaterialDiffuseTileRendererKeys.TerrainControlMap, data.GpuTextureManager!.ControlMap.AtlasTexture);
        Parameters.Set(TerrainMaterialDiffuseTileRendererKeys.DiffuseRoughnessArray, MaterialDiffuseRoughnessArray);

        PrepareDraw(context);
        commandList.Draw(3);

        // Update atlas
        streamingTextureAtlas.UpdateChunkFromStaging(context.GraphicsContext, textureIndex);
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
