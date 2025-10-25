using Stride.Engine;
using Stride.Games;
using StrideCommunity.ImGuiDebug;
using StrideTerrain.Sample.Game;
using StrideTerrain.TerrainSystem;
using StrideTerrain.Vegetation;
using System.Linq;
using Stride.Graphics;

namespace StrideTerrain.Sample;

public class SampleGame : Stride.Engine.Game
{
    private GameSessionSystem? _gameSessionSystem = null;

    protected override void Initialize()
    {
        base.Initialize();

        // Set the window in a sane position
        // TODO: This should center the window instead
        Window.Position = new Stride.Core.Mathematics.Int2(10, 10);
        //Window.FullscreenIsBorderlessWindow = true;

        var coreDataSettings = Settings.Configurations.Get<CoreDataSettings>();

        _gameSessionSystem = new GameSessionSystem(Services, SceneSystem, Script, coreDataSettings);
        GameSystems.Add(_gameSessionSystem);
        Services.AddService(_gameSessionSystem);
    }

    protected override void BeginRun()
    {
        base.BeginRun();

        // Fix Update order
        ((GameSystemBase)GameSystems.First(x => x is InputSystem)).UpdateOrder = -2;

        var imGuiSystem = new ImGuiSystem(Services, GraphicsDeviceManager)
        {
            UpdateOrder = -1
        };

        new HierarchyView(Services);
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
