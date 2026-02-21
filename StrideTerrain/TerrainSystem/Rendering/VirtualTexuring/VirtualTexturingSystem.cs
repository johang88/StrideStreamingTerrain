using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public class VirtualTexturingSystem : IDisposable
{
    public FeedbackReader FeedbackReader { get; }
    public FeedbackRenderer FeedbackRenderer { get; }
    public RequestProcessor RequestProcessor { get; } = new();
    public PhysicalAtlas PhysicalAtlas { get; }
    public TileRenderer TileRenderer { get; }

    public VirtualTexturingSystem(IServiceRegistry services, GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
    {
        FeedbackRenderer = new(services, graphicsDevice, screenWidth, screenHeight);
        FeedbackReader = new(graphicsDevice, screenWidth, screenHeight, FeedbackRenderer.FeedbackRenderTarget);
        PhysicalAtlas = new(graphicsDevice);
        TileRenderer = new(services, graphicsDevice, PhysicalAtlas);
    }

    public void Update(RenderDrawContext context, CameraComponent camera, TerrainRuntimeData terrainRuntimeData)
    {
        var cameraPosition = camera.GetWorldPosition();

        FeedbackRenderer.Draw(context, camera, terrainRuntimeData);

        RequestProcessor.CameraPositionXZ = new Vector2(cameraPosition.X, cameraPosition.Z);

        var rawRequests = FeedbackReader.ReadFeedback(context.CommandList);
        var tilesToRender = RequestProcessor.ProcessRequests(rawRequests);

        //if (tilesToRender.Count == 0)
        //{
        //    tilesToRender.Add(new()
        //    {
        //        TileX = 32,
        //        TileY = 32,
        //        MipLevel = 0
        //    });
        //}

        if (tilesToRender.Count > 0)
        {
            TileRenderer.RenderTiles(context, tilesToRender, RequestProcessor, terrainRuntimeData);
        }

        FeedbackReader.SubmitReadback(context.CommandList);
    }

    public void Dispose()
    {
        FeedbackReader?.Dispose();
        PhysicalAtlas?.Dispose();
    }
}