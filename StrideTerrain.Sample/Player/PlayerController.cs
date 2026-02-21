using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using StrideCommunity.ImGuiDebug;
using StrideTerrain.Sample.Actors;
using StrideTerrain.Sample.Game;
using StrideTerrain.TerrainSystem;
namespace StrideTerrain.Sample.Player;

public class PlayerController : SyncScript
{
    private ActorController _actorController = null!;

    public ThirdPersonCameraController? CameraController { get; set; }

    public float DeadZone { get; set; } = 0.25f;
    public bool Enabled { get; set; } = true;

    private PlayerInterface? _playerInterface;
    private MapMetaDataComponentProcessor? _metaDataProcessor;

    public override void Start()
    {
        base.Start();

        _actorController = Entity.Get<ActorController>();
        _playerInterface = new(Services);
    }

    public override void Cancel()
    {
        base.Cancel();

        _playerInterface?.Dispose();
        _playerInterface = null;
    }

    public override void Update()
    {
        if (!Enabled || CameraController == null)
        {
            if (Input.IsMousePositionLocked)
            {
                Input.UnlockMousePosition();
                Game.IsMouseVisible = true;
            }

            return;
        }

        _metaDataProcessor ??= SceneSystem.SceneInstance.GetProcessor<MapMetaDataComponentProcessor>();

        _playerInterface!.MiniMap = _metaDataProcessor?.Current?.MiniMap;
        _playerInterface.PlayerTransform = Entity.Transform;
        _playerInterface.PlayerRotationTransform = Entity.Transform;
        _playerInterface.PlayerCameraRotationTransform = CameraController.Entity.Transform;

        if (!_playerInterface.ShowFullMap)
        {
            UpdateInGame();
        }

        if (Input.IsKeyPressed(Keys.M))
        {
            _playerInterface.ShowFullMap = !_playerInterface.ShowFullMap;
            _playerInterface.ShowCompass = !_playerInterface.ShowCompass;
        }
    }

    private void UpdateInGame()
    {
        var inputMoveDirection = Vector2.Zero;
        if (Input.IsKeyDown(Keys.A))
            inputMoveDirection += -Vector2.UnitX;
        if (Input.IsKeyDown(Keys.D))
            inputMoveDirection += +Vector2.UnitX;
        if (Input.IsKeyDown(Keys.W))
            inputMoveDirection += +Vector2.UnitY;
        if (Input.IsKeyDown(Keys.S))
            inputMoveDirection += -Vector2.UnitY;

        var newMoveDirection = Utils.LogicDirectionToWorldDirection(inputMoveDirection, CameraController!.Camera, Vector3.UnitY);
        _actorController.SetInput(newMoveDirection);

        _actorController.ActorStats.MovementSpeed = Input.IsKeyDown(Keys.LeftShift) ? 100 : 5;

        UpdateCameraInput();
    }

    private void UpdateCameraInput()
    {
        var cameraDirection = Vector3.Zero;
        if (Input.IsMouseButtonDown(MouseButton.Right))
        {
            Input.LockMousePosition(true);
            Game.IsMouseVisible = false;
        }
        else
        {
            Input.UnlockMousePosition();
            Game.IsMouseVisible = true;
        }

        if (Input.IsMousePositionLocked)
        {
            cameraDirection += new Vector3(Input.MouseDelta.X, -Input.MouseDelta.Y, 0.0f) * 100.0f;
        }

        CameraController!.SetInput(cameraDirection);
    }
}