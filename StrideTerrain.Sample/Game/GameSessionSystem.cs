using Stride.Core;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;

namespace StrideTerrain.Sample.Game;

class GameSessionSystem : GameSystemBase
{
    private readonly SceneSystem _sceneSystem;
    private readonly ScriptSystem _scriptSystem;
    private readonly CoreDataSettings _coreDataSettings;

    public GameSession? Current { get; private set; }

    public GameSessionSystem(IServiceRegistry registry, SceneSystem sceneSystem, ScriptSystem scriptSystem, CoreDataSettings coreDataSettings)
        : base(registry)
    {
        _sceneSystem = sceneSystem;
        _scriptSystem = scriptSystem;
        _coreDataSettings = coreDataSettings;

        Enabled = true;
    }

    public void CreateNewSession()
    {
        DestroyActiveSession();

        Current = new GameSession(Content, _sceneSystem, _scriptSystem, _coreDataSettings);
        Current.ChangeScene(_coreDataSettings.InitialScene.Url);
    }

    public void DestroyActiveSession()
    {
        Current?.Dispose();
        Current = null;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        Current?.Update(gameTime);
    }
}