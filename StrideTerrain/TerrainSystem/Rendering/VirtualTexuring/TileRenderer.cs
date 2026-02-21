using SharpFont;
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
/// Each tile is rendered as a fullscreen quad over the tile's world-space region,
/// evaluating EvaluateTerrainMaterial() per texel.
/// </summary>
public class TileRenderer : DynamicEffectRenderer
{
    private readonly PhysicalAtlas _atlas;

    // Staging render targets (uncompressed, tile-sized)
    private readonly Texture _stagingDiffuse;  // RGBA8, 264×264
    private readonly Texture _stagingNormal;   // RG16F, 264×264
    private readonly Texture _stagingRoughness;// R8, 264×264

    public Texture? MaterialDiffuseRoughnessArray { get; set; }
    public Texture? MaterialNormalArray { get; set; }

    private Texture[] _renderTargets;

    public TileRenderer(IServiceRegistry services, GraphicsDevice graphicsDevice, PhysicalAtlas atlas)
        : base(services, graphicsDevice, "TerrainMaterialTileRenderer")
    {
        _atlas = atlas;

        int padded = VTConstants.TileSizePadded; // 264

        _stagingDiffuse = Texture.New2D(graphicsDevice, padded, padded, PixelFormat.R8G8B8A8_UNorm_SRgb, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        _stagingNormal = Texture.New2D(graphicsDevice, padded, padded, PixelFormat.R16G16_Float, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        _stagingRoughness = Texture.New2D(graphicsDevice, padded, padded, PixelFormat.R8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        _renderTargets = [_stagingDiffuse, _stagingNormal, _stagingRoughness];
    }

    protected override void ConfigurePipelineState(CommandList commandList)
    {
        base.ConfigurePipelineState(commandList);

        PipelineState.State.DepthStencilState.DepthBufferEnable = false;
        PipelineState.State.RasterizerState.CullMode = CullMode.None;
    }

    /// <summary>
    /// Render a batch of tiles into the physical atlas.
    /// </summary>
    public void RenderTiles(RenderDrawContext context, List<TileRequest> requests, RequestProcessor requestProcessor, TerrainRuntimeData data)
    {
        if (MaterialDiffuseRoughnessArray == null || MaterialNormalArray == null)
            return;

        var commandList = context.CommandList;

        using var _ = context.PushRenderTargetsAndRestore();

        foreach (var req in requests)
        {
            long key = RequestProcessor.TileKey(req.TileX, req.TileY, req.MipLevel);
            int slotIdx = _atlas.AllocateSlot(key);

            // Compute world-space bounds for this tile
            // Map directly to the tile's actual world extent. Border pixels (0-3 and 260-263) will fall
            // slightly outside and sample from adjacent terrain regions naturally.
            float tileWorldSize = VTConstants.BaseTileWorld * MathF.Pow(2, req.MipLevel);

            float worldMinX = req.TileX * tileWorldSize;
            float worldMinZ = req.TileY * tileWorldSize;
            float worldMaxX = (req.TileX + 1) * tileWorldSize;
            float worldMaxZ = (req.TileY + 1) * tileWorldSize;

            //--- Step 1: Render to staging targets ---
            commandList.SetRenderTargets(null, _renderTargets);
            commandList.SetViewport(new Viewport(0, 0, VTConstants.TileSizePadded, VTConstants.TileSizePadded));

            // Set shader parameters
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

            // Draw fullscreen quad — the pixel shader evaluates the terrain material
            commandList.Draw(3);

            //--- Step 2: Copy staging → atlas at the tile's slot region ---
            var region = PhysicalAtlas.GetSlotRegion(slotIdx);

            commandList.CopyRegion(
                _stagingDiffuse, 0,
                new ResourceRegion(0, 0, 0,
                    VTConstants.TileSizePadded, VTConstants.TileSizePadded, 1),
                _atlas.DiffuseAtlas, 0,
                region.X, region.Y, 0);

            commandList.CopyRegion(
                _stagingNormal, 0,
                new ResourceRegion(0, 0, 0,
                    VTConstants.TileSizePadded, VTConstants.TileSizePadded, 1),
                _atlas.NormalAtlas, 0,
                region.X, region.Y, 0);

            commandList.CopyRegion(
                _stagingRoughness, 0,
                new ResourceRegion(0, 0, 0,
                    VTConstants.TileSizePadded, VTConstants.TileSizePadded, 1),
                _atlas.RoughnessAtlas, 0,
                region.X, region.Y, 0);

            //--- Step 3: Update bookkeeping ---
            _atlas.MarkResident(slotIdx, req.TileX, req.TileY, req.MipLevel);
            requestProcessor.MarkResident(key);
        }

        // Flush all indirection updates to GPU
        _atlas.FlushIndirection(commandList);
    }

    public override void Dispose()
    {
        base.Dispose();

        _stagingDiffuse?.Dispose();
        _stagingNormal?.Dispose();
        _stagingRoughness?.Dispose();
    }
}