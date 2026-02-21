using SharpDX.Direct3D11;
using SharpDX.Direct3D12;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Shaders.Convertor;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Shaders;
using StrideTerrain.Rendering;
using StrideTerrain.TerrainSystem.Effects;
using StrideTerrain.TerrainSystem.Effects.Material;
using System;
using Buffer = Stride.Graphics.Buffer;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public class FeedbackRenderer : DynamicEffectRenderer
{
    private static readonly ProfilingKey ProfilingKeyRenderFeedback = new("Terrain.VT.RenderFeedback");

    public Texture FeedbackRenderTarget { get; }
    private readonly Texture _depthBuffer;
    private readonly Buffer _emptyBuffer;

    public FeedbackRenderer(IServiceRegistry services, GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        : base(services, graphicsDevice, "TerrainVirtualTextureFeedback")
    {
        var width = screenWidth / 4;
        var height = screenHeight / 4;

        FeedbackRenderTarget = Texture.New2D(graphicsDevice, width, height, PixelFormat.R8G8B8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        _depthBuffer = Texture.New2D(graphicsDevice, width, height, PixelFormat.D32_Float_S8X24_UInt, TextureFlags.DepthStencil);
        _emptyBuffer = Buffer.Vertex.New(graphicsDevice, new Vector4[1]);
    }

    protected override void ConfigurePipelineState(Stride.Graphics.CommandList commandList)
    {
        base.ConfigurePipelineState(commandList);

        PipelineState.State.DepthStencilState.DepthBufferFunction = CompareFunction.GreaterEqual;
    }

    public void Draw(RenderDrawContext context, CameraComponent camera, TerrainRuntimeData data)
    {
        var view = camera.ViewMatrix;

        Matrix reverseZMatrix =
            new(1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, -1.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 1.0f);

        var projection = Matrix.Multiply(camera.ProjectionMatrix, reverseZMatrix);
        Matrix.Multiply(ref view, ref projection, out var viewProjection);
        
        var commandList = context.CommandList;
        var graphicsDevice = context.GraphicsDevice;

        // Set render target
        context.PushRenderTargetsAndRestore();

        commandList.SetRenderTarget(_depthBuffer, FeedbackRenderTarget);
        commandList.SetViewport(new(0, 0, FeedbackRenderTarget.Width, FeedbackRenderTarget.Height));

        commandList.Clear(_depthBuffer, DepthStencilClearOptions.DepthBuffer, 0);
        commandList.Clear(FeedbackRenderTarget, Color4.Black);

        var meshDraw = data.MeshManager!.Mesh.Draw;
        var instanceCount = data.MeshManager!.PrepareDraw(context.CommandList, viewProjection, view, false);

        // Prepare effect parameters
        Parameters.Set(TerrainDisplacementKeys.ChunkInstanceData, data.MeshManager.ChunkInstanceDataBuffer);
        Parameters.Set(TerrainDataKeys.ChunkBuffer, data.MeshManager.ChunkBuffer);
        Parameters.Set(TerrainDataKeys.Heightmap, data.GpuTextureManager!.Heightmap.AtlasTexture);

        Parameters.Set(TerrainDataKeys.UnitsPerTexel, data.UnitsPerTexel);
        Parameters.Set(TerrainDataKeys.InvTerrainTextureSize, TerrainRuntimeData.InvRuntimeTextureSize);
        Parameters.Set(TerrainDataKeys.MaxHeight, data.TerrainData.Header.MaxHeight);
        Parameters.Set(TerrainDataKeys.ChunkSize, (uint)data.TerrainData.Header.ChunkSize);
        Parameters.Set(TerrainDataKeys.InvTerrainSize, 1.0f / (data.TerrainData.Header.Size * data.UnitsPerTexel));

        Parameters.Set(TransformationKeys.World, Matrix.Identity);
        Parameters.Set(TransformationKeys.WorldViewProjection, viewProjection);

        Parameters.Set(TerrainVirtualTextureKeys.VTMipBias, VTConstants.MipBias);
        Parameters.Set(TerrainVirtualTextureKeys.VTMaxAniso, 4.0f);
        Parameters.Set(TerrainVirtualTextureKeys.VTCameraPosition, camera.GetWorldPosition());

        float terrainSize = data.TerrainData.Header.Size * data.UnitsPerTexel;
        Parameters.Set(TerrainVirtualTextureKeys.VTResolution, (terrainSize / VTConstants.BaseTileWorld) * VTConstants.TileSize);

        PrepareDraw(context);

        // Draw
        commandList.SetVertexBuffer(0, _emptyBuffer, 0, 0); // Could we just do null, do we even care?

        using var _ = context.QueryManager.BeginProfile(Color4.Black, ProfilingKeyRenderFeedback);
        commandList.DrawInstanced(meshDraw.DrawCount, instanceCount, 0);
        context.PopRenderTargets();
    }

    public override void Dispose()
    {
        base.Dispose();
        _emptyBuffer.Dispose();
    }
}