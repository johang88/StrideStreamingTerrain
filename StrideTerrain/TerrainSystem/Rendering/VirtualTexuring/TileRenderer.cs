using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using StrideTerrain.Rendering;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Effects.Material;
using System;
using System.Collections.Generic;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

/// <summary>
/// Renders terrain material into physical atlas tiles.
/// Each tile is rendered as a fullscreen triangle over the tile's world-space region
/// (including a border expansion so the 4-pixel padding contains real neighbour data).
/// </summary>
public class TileRenderer : DynamicEffectRenderer
{
    private readonly PhysicalAtlas _atlas;

    private readonly Texture _stagingDiffuse;    // RGBA8 sRGB, TileSizePadded×TileSizePadded
    private readonly Texture _stagingNormal;     // RG16F
    private readonly Texture _stagingRoughness;  // R8

    public Texture? MaterialDiffuseRoughnessArray { get; set; }
    public Texture? MaterialNormalArray { get; set; }

    private readonly Texture[] _renderTargets;

    public TileRenderer(IServiceRegistry services, GraphicsDevice graphicsDevice, PhysicalAtlas atlas)
        : base(services, graphicsDevice, "TerrainMaterialTileRenderer")
    {
        _atlas = atlas;

        int padded = VTConstants.TileSizePadded;

        _stagingDiffuse = Texture.New2D(graphicsDevice, padded, padded,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        _stagingNormal = Texture.New2D(graphicsDevice, padded, padded,
            PixelFormat.R16G16_Float,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        _stagingRoughness = Texture.New2D(graphicsDevice, padded, padded,
            PixelFormat.R8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        _renderTargets = [_stagingDiffuse, _stagingNormal, _stagingRoughness];
    }

    protected override void ConfigurePipelineState(CommandList commandList)
    {
        base.ConfigurePipelineState(commandList);

        PipelineState.State.DepthStencilState.DepthBufferEnable = false;
        PipelineState.State.RasterizerState.CullMode = CullMode.None;
    }

    public void RenderTiles(RenderDrawContext context, List<TileRequest> requests, TerrainRuntimeData data)
    {
        if (MaterialDiffuseRoughnessArray == null || MaterialNormalArray == null)
            return;

        var commandList = context.CommandList;

        using var _ = context.PushRenderTargetsAndRestore();

        foreach (var req in requests)
        {
            int slotIdx = PhysicalAtlas.GetToroidalSlot(req.TileX, req.TileY, req.MipLevel);

            // World-space bounds for this tile, expanded by one border width on each side
            // so the 4-pixel padding contains real terrain data from neighbouring tiles.
            float tileWorldSize = VTConstants.BaseTileWorld * MathF.Pow(2, req.MipLevel);
            float borderWorld = tileWorldSize / VTConstants.TileSize * VTConstants.TileBorder;

            float worldMinX = req.TileX * tileWorldSize - borderWorld;
            float worldMinZ = req.TileY * tileWorldSize - borderWorld;
            float worldMaxX = (req.TileX + 1) * tileWorldSize + borderWorld;
            float worldMaxZ = (req.TileY + 1) * tileWorldSize + borderWorld;

            //--- Render to staging targets ---
            commandList.SetRenderTargets(null, _renderTargets);
            commandList.SetViewport(new Viewport(0, 0, VTConstants.TileSizePadded, VTConstants.TileSizePadded));

            Parameters.Set(TerrainMaterialTileRendererKeys.WorldBoundsMin, new Vector2(worldMinX, worldMinZ));
            Parameters.Set(TerrainMaterialTileRendererKeys.WorldBoundsMax, new Vector2(worldMaxX, worldMaxZ));
            Parameters.Set(TerrainMaterialSamplingKeys.DiffuseRoughnessArray, MaterialDiffuseRoughnessArray);
            Parameters.Set(TerrainMaterialSamplingKeys.NormalArray, MaterialNormalArray);

            Parameters.Set(TerrainDataKeys.ChunkBuffer, data.MeshManager!.ChunkBuffer);
            Parameters.Set(TerrainDataKeys.SectorToChunkMapBuffer, data.MeshManager!.SectorToChunkMapBuffer);
            Parameters.Set(TerrainDataKeys.Heightmap, data.GpuTextureManager!.Heightmap.AtlasTexture);
            Parameters.Set(TerrainDataKeys.TerrainNormalMap, data.GpuTextureManager!.NormalMap.AtlasTexture);
            Parameters.Set(TerrainDataKeys.TerrainControlMap, data.GpuTextureManager!.ControlMap.AtlasTexture);
            Parameters.Set(TerrainDataKeys.ChunksPerRow, (uint)data.ChunksPerRowLod0);
            Parameters.Set(TerrainDataKeys.UnitsPerTexel, data.UnitsPerTexel);
            Parameters.Set(TerrainDataKeys.InvUnitsPerTexel, 1.0f / data.UnitsPerTexel);
            Parameters.Set(TerrainDataKeys.TerrainTextureSize, TerrainRuntimeData.RuntimeTextureSize);
            Parameters.Set(TerrainDataKeys.InvTerrainTextureSize, TerrainRuntimeData.InvRuntimeTextureSize);
            Parameters.Set(TerrainDataKeys.MaxHeight, data.TerrainData.Header.MaxHeight);
            Parameters.Set(TerrainDataKeys.ChunkSize, (uint)data.TerrainData.Header.ChunkSize);
            Parameters.Set(TerrainDataKeys.InvTerrainSize, 1.0f / (data.TerrainData.Header.Size * data.UnitsPerTexel));

            PrepareDraw(context);
            commandList.Draw(3);

            //--- Copy staging → atlas slot ---
            var region = PhysicalAtlas.GetSlotRegion(slotIdx);

            commandList.CopyRegion(
                _stagingDiffuse, 0,
                new ResourceRegion(0, 0, 0, VTConstants.TileSizePadded, VTConstants.TileSizePadded, 1),
                _atlas.DiffuseAtlas, 0,
                region.X, region.Y, 0);

            commandList.CopyRegion(
                _stagingNormal, 0,
                new ResourceRegion(0, 0, 0, VTConstants.TileSizePadded, VTConstants.TileSizePadded, 1),
                _atlas.NormalAtlas, 0,
                region.X, region.Y, 0);

            commandList.CopyRegion(
                _stagingRoughness, 0,
                new ResourceRegion(0, 0, 0, VTConstants.TileSizePadded, VTConstants.TileSizePadded, 1),
                _atlas.RoughnessAtlas, 0,
                region.X, region.Y, 0);
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        _stagingDiffuse?.Dispose();
        _stagingNormal?.Dispose();
        _stagingRoughness?.Dispose();
    }
}
