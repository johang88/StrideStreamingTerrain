using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering.Compositing;
using StrideCommunity.ImGuiDebug;
using StrideTerrain.Rendering;
using StrideTerrain.TerrainSystem;
using System.Linq;

namespace StrideTerrain.Sample;

public class SampleGame : Game
{
    protected override void BeginRun()
    {
        base.BeginRun();
        _ = new ImGuiSystem(Services, GraphicsDeviceManager);

        //new PerfMonitor(Services);
        new HierarchyView(Services);
        Inspector.FindFreeInspector(Services).Target = SceneSystem.SceneInstance.RootScene.Entities.FirstOrDefault(x => x.Name == "Terrain")?.Get<TerrainComponent>();

        //var reverseZRenderer = (ReverseZRenderer)((SceneRendererCollection)((SceneCameraRenderer)SceneSystem.GraphicsCompositor.Game).Child).Children.First();
        //var forwardRenderer = (ForwardRenderer)reverseZRenderer.Child;
        //Inspector.FindFreeInspector(Services).Target = forwardRenderer.PostEffects;
    }

    public override void ConfirmRenderingSettings(bool gameCreation)
    {
        base.ConfirmRenderingSettings(gameCreation);

        var deviceManager = (GraphicsDeviceManager)graphicsDeviceManager;
        deviceManager.PreferredDepthStencilFormat = PixelFormat.D32_Float_S8X24_UInt;

        //Profiler.EnableAll();
        //GraphicsDeviceManager.DeviceCreationFlags |= DeviceCreationFlags.Debug;
    }
}
