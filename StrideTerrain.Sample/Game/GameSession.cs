using Hexa.NET.ImGui;
using Stride.Core;
using Stride.Core.Serialization.Contents;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using StrideCommunity.ImGuiDebug;
using StrideTerrain.Sample.Actors;
using StrideTerrain.Sample.Player;
using StrideTerrain.TerrainSystem;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Sample.Game;

public class GameSession : IDisposable
{
    public Scene? CurrentScene { get; private set; }

    public Entity Player { get; set; }
    public Entity Camera { get; set; }

    public IContentManager Content { get; }
    public SceneSystem SceneSystem { get; }
    public ScriptSystem ScriptSystem { get; }

    private readonly Scene _rootScene;
    private readonly CoreDataSettings _coreDataSettings;

    private State _state;
    private LoadingScreen _loadingScreen;
    private CameraComponent? _existingCamera;

    public GameSession(IContentManager contentManager, SceneSystem sceneSystem, ScriptSystem scriptSystem, CoreDataSettings coreDataSettings)
    {
        Content = contentManager;
        SceneSystem = sceneSystem;
        ScriptSystem = scriptSystem;

        _coreDataSettings = coreDataSettings;

        _rootScene = sceneSystem.SceneInstance.RootScene;

        // Set up core objects
        var playerPrefab = contentManager.Load<Prefab>(_coreDataSettings.Player.Url);
        var cameraPrefab = contentManager.Load<Prefab>(_coreDataSettings.PlayerCamera.Url);

        Player = playerPrefab.Instantiate()[0];
        Player.Add(new PlayerController());
        Camera = cameraPrefab.Instantiate()[0];

        _loadingScreen = new LoadingScreen(SceneSystem.Services);
    }

    public void Dispose()
    {
        Leave();

        _rootScene.Entities.Remove(Player);
        _rootScene.Entities.Remove(Camera);

        _loadingScreen.Dispose();
    }

    public void Update(GameTime gameTime)
    {
    }

    public void ChangeScene(string url, string? entryPointName = null)
    {
        if (_state == State.Loading)
            throw new InvalidOperationException("Already loading scene!");

        _loadingScreen.Show();

        Leave();

        _state = State.Loading;

        ScriptSystem.Scheduler.Add(async () =>
        {
            CurrentScene = await Content.LoadAsync<Scene>(url);
            _rootScene.Children.Add(CurrentScene);

            await ScriptSystem.NextFrame();

            var entryPointProcessor = SceneSystem.SceneInstance.GetProcessor<EntryPointProcessor>();
            var (position, rotation) = entryPointProcessor.GetEntryPoint(entryPointName);

            await ScriptSystem.NextFrame();

            var terrainProcessor = await WaitUntilAvailable(() => SceneSystem.SceneInstance.GetProcessor<TerrainProcessor>());

            // Wait until terrain is loaded in full resolution and physics is ready
            terrainProcessor.OverrideCameraPosition = position;
            await WaitUntil(() => terrainProcessor.TerrainData?.MeshManager?.IsReady == true);
            await WaitUntil(() => terrainProcessor.TerrainData?.GetAtlasUv(position.X, position.Z).Lod == 0);
            await WaitUntil(() => terrainProcessor.TerrainData?.PhysicsManager?.IsLoaded(position) == true);
            await WaitUntil(() => terrainProcessor.TerrainData?.VirtualTexturingSystem != null);
            terrainProcessor.OverrideCameraPosition = null;
            
            terrainProcessor.TerrainData!.VirtualTexturingSystem!.InvalidateAll();

            AttachPlayerAndCamera();

            // Keeps fallin gthrough the floor if we don't do it like this for some reaason ... even though everything should be loaded ...
            for (var i = 0; i < 60; i++)
            {
                Player.Get<ActorController>().SetPositionAndRotation(position, rotation);
                await ScriptSystem.NextFrame();
            }

            Player.Get<ActorController>().SetPositionAndRotation(position, rotation);

            _loadingScreen.Hide();
            _state = State.Loaded;
        });
    }

    private async Task WaitUntil(Func<bool> check)
    {
        do
        {
            await ScriptSystem.NextFrame();
        } while (!check());
    }

    private async Task<T> WaitUntilAvailable<T>(Func<T?> check) where T : class
    {
        T? result;
        do
        {
            await ScriptSystem.NextFrame();
            result = check();
        } while (result == null);
        return result;
    }

    private void AttachPlayerAndCamera()
    {
        if (Player.Scene == null)
        {
            _existingCamera ??= _rootScene.Entities.FirstOrDefault(x => x.Name == "Camera")?.Get<CameraComponent>();
            if (_existingCamera != null)
            {
                _existingCamera.Enabled = false;
            }

            Player.Get<PlayerController>().CameraController = Camera.Get<ThirdPersonCameraController>();
            Camera.Get<ThirdPersonCameraController>().SetTarget(Player.Transform);

            _rootScene.Entities.Add(Player);
            _rootScene.Entities.Add(Camera);
        }
    }

    public void Leave()
    {
        if (CurrentScene == null)
            return;

        _rootScene.Children.Remove(CurrentScene);
        Content.Unload(CurrentScene);
        CurrentScene = null;

        _rootScene.Entities.Remove(Player);
        _rootScene.Entities.Remove(Camera);

        _state = State.Unloaded;
    }

    private enum State
    {
        Unloaded,
        Loading,
        Loaded
    }

    // Does not really belong here but whatever ...
    private class LoadingScreen(IServiceRegistry services) : BaseWindow(services)
    {
        private float _time;
        private float _alpha = 0f;
        private bool _visible = false;

        protected override ImGuiWindowFlags WindowFlags =>
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoInputs;

        protected override Vector2? WindowSize => new(Game.Window.ClientBounds.Width, Game.Window.ClientBounds.Height);
        protected override Vector2? WindowPos => new(0, 0);

        public void Show()
        {
            _visible = true;
        }

        public void Hide()
        {
            _visible = false;
        }

        protected override void OnDestroy() { }

        protected override void OnDraw(bool collapsed)
        {
            var drawList = GetWindowDrawList();
            var size = WindowSize ?? Vector2.Zero;
            var center = size - new Vector2(128, 128);

            float dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            _time += dt;

            // Fade transition speed
            const float fadeSpeed = 2.5f;

            // Smooth fade in/out alpha
            float targetAlpha = _visible ? 1f : 0f;
            _alpha = Math.Clamp(_alpha + (targetAlpha - _alpha) * dt * fadeSpeed, 0f, 1f);

            if (_alpha <= 0.001f && !_visible)
            {
                return; // fully transparent, nothing to draw
            }

            // Draw black overlay with fade
            drawList.AddRectFilled(
                Vector2.Zero,
                size,
                ColorConvertFloat4ToU32(new Vector4(0, 0, 0, _alpha))
            );

            // Only draw pulsating squares while visible (not fading out)
            if (!_visible)
                return;

            // Layout + animation parameters
            const float spacing = 50f;       // distance between square centers
            const float baseSize = 14f;      // base half-size
            const float pulseAmount = 8f;    // how much they grow/shrink
            const float pulseSpeed = 3f;     // pulsing speed

            Vector2[] offsets =
            [
                new(-spacing / 2, -spacing / 2),
                new(spacing / 2, -spacing / 2),
                new(-spacing / 2,  spacing / 2),
                new(spacing / 2,   spacing / 2)
            ];

            for (int i = 0; i < 4; i++)
            {
                float phase = _time * pulseSpeed + i * MathF.PI / 2;
                float scale = baseSize + MathF.Sin(phase) * pulseAmount;
                float brightness = 0.6f + 0.4f * ((scale - baseSize) / pulseAmount + 1f) * 0.5f;

                var color = new Vector4(brightness, brightness, brightness, _alpha);
                var pos = center + offsets[i];
                var pMin = pos - new Vector2(scale);
                var pMax = pos + new Vector2(scale);

                drawList.AddRectFilled(pMin, pMax, ColorConvertFloat4ToU32(color), 4f);
            }
        }
    }

}