using SharpFont.PostScript;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using StrideTerrain.Rendering;
using StrideTerrain.Rendering.Effects;
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

    private readonly Texture _stagingDiffuse;
    private readonly Texture _stagingDiffuseTargetView;
    private readonly Texture _stagingDiffuseCompressView;

    private readonly Texture _stagingNormal;

    public Texture? MaterialDiffuseRoughnessArray { get; set; }
    public Texture? MaterialNormalArray { get; set; }

    private readonly Texture[] _renderTargets;

    private ComputeEffectShader? _blockCompressBc1;
    private readonly Texture _compressionTargetBc1;

    private ComputeEffectShader? _blockCompressBc3;
    private readonly Texture _compressionTargetBc3;

    private ComputeEffectShader? _blockCompressBc5;
    private readonly Texture _compressionTargetBc5;

    public TileRenderer(IServiceRegistry services, GraphicsDevice graphicsDevice, PhysicalAtlas atlas)
        : base(services, graphicsDevice, "TerrainMaterialTileRenderer")
    {
        _atlas = atlas;

        int padded = VTConstants.TileSizePadded;

        _stagingDiffuse = Texture.New2D(graphicsDevice, padded, padded,
            PixelFormat.R8G8B8A8_Typeless,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        _stagingDiffuseTargetView = _stagingDiffuse.ToTextureView(new()
        {
            Flags = TextureFlags.RenderTarget,
            Format = PixelFormat.R8G8B8A8_UNorm_SRgb,
            Type = ViewType.Single
        });

        _stagingDiffuseCompressView = _stagingDiffuse.ToTextureView(new()
        {
            Flags = TextureFlags.ShaderResource,
            Format = PixelFormat.R8G8B8A8_UNorm,
            Type = ViewType.Single
        });

        _stagingNormal = Texture.New2D(graphicsDevice, padded, padded,
            PixelFormat.R16G16_Float,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        _compressionTargetBc1 = Texture.New2D(graphicsDevice, 66, 66, PixelFormat.R32G32_UInt, TextureFlags.UnorderedAccess);
        _compressionTargetBc3 = Texture.New2D(graphicsDevice, 66, 66, PixelFormat.R32G32B32A32_UInt, TextureFlags.UnorderedAccess);
        _compressionTargetBc5 = Texture.New2D(graphicsDevice, 66, 66, PixelFormat.R32G32B32A32_UInt, TextureFlags.UnorderedAccess);

        _renderTargets = [_stagingDiffuseTargetView, _stagingNormal];
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

        _blockCompressBc1 ??= new(context.RenderContext)
        {
            ShaderSourceName = "CompressBC1"
        };

        _blockCompressBc3 ??= new(context.RenderContext)
        {
            ShaderSourceName = "CompressBC3"
        };

        _blockCompressBc5 ??= new(context.RenderContext)
        {
            ShaderSourceName = "CompressBC5"
        };

        var commandList = context.CommandList;

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
            using (context.PushRenderTargetsAndRestore())
            {
                commandList.SetRenderTargets(null, _renderTargets);
                commandList.SetViewport(new Viewport(0, 0, VTConstants.TileSizePadded, VTConstants.TileSizePadded));

                Parameters.Set(TerrainMaterialTileRendererKeys.WorldBoundsMin, new Vector2(worldMinX, worldMinZ));
                Parameters.Set(TerrainMaterialTileRendererKeys.WorldBoundsMax, new Vector2(worldMaxX, worldMaxZ));
                Parameters.Set(TerrainMaterialTileRendererKeys.MipLevel, req.MipLevel);
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
            }

            //--- Copy staging → atlas slot ---
            var region = PhysicalAtlas.GetSlotRegion(slotIdx);

            CompressBC3(context, _stagingDiffuseCompressView, _compressionTargetBc3);
            commandList.CopyRegion(
                _compressionTargetBc3, 0,
                new ResourceRegion(0, 0, 0, 66, 66, 1),
                _atlas.DiffuseRoughnessAtlas, 0,
                region.X, region.Y, 0);

            CompressBC5(context, _stagingNormal, _compressionTargetBc5);
            commandList.CopyRegion(
                _compressionTargetBc5, 0,
                new ResourceRegion(0, 0, 0, 66, 66, 1),
                _atlas.NormalAtlas, 0,
                region.X, region.Y, 0);
        }
    }

    private void CompressBC1(RenderDrawContext context, Texture source, Texture target)
    {
        _blockCompressBc1!.Parameters.Set(CompressBC1Keys.InputTexture, source);
        _blockCompressBc1.Parameters.Set(CompressBC1Keys.OutputTexture, target);
        _blockCompressBc1.Parameters.Set(CompressBC1Keys.InvTextureWidth, 1.0f / source.Width);

        _blockCompressBc1.ThreadNumbers = new Int3(6, 6, 1);
        _blockCompressBc1.ThreadGroupCounts = new Int3(11, 11, 1);

        _blockCompressBc1.Draw(context, "Compress.BC1");
    }

    private void CompressBC3(RenderDrawContext context, Texture source, Texture target)
    {
        _blockCompressBc3!.Parameters.Set(CompressBC3Keys.InputTexture, source);
        _blockCompressBc3.Parameters.Set(CompressBC3Keys.OutputTexture, target);
        _blockCompressBc3.Parameters.Set(CompressBC3Keys.InvTextureWidth, 1.0f / source.Width);

        _blockCompressBc3.ThreadNumbers = new Int3(6, 6, 1);
        _blockCompressBc3.ThreadGroupCounts = new Int3(11, 11, 1);

        _blockCompressBc3.Draw(context, "Compress.BC3");
    }

    private void CompressBC5(RenderDrawContext context, Texture source, Texture target)
    {
        _blockCompressBc5!.Parameters.Set(CompressBC5Keys.InputTexture, source);
        _blockCompressBc5.Parameters.Set(CompressBC5Keys.OutputTexture, target);
        _blockCompressBc5.Parameters.Set(CompressBC5Keys.InvTextureWidth, 1.0f / source.Width);

        _blockCompressBc5.ThreadNumbers = new Int3(6, 6, 1);
        _blockCompressBc5.ThreadGroupCounts = new Int3(11, 11, 1);

        _blockCompressBc5.Draw(context, "Compress.BC5");
    }

    public override void Dispose()
    {
        base.Dispose();

        _blockCompressBc1?.Dispose();
        _stagingDiffuse?.Dispose();
        _stagingNormal?.Dispose();
    }
}
