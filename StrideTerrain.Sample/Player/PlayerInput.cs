using System.Collections.Generic;
using System.Linq;
using Hexa.NET.ImGui;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Events;
using Stride.Input;
using Stride.Physics;
using StrideTerrain.TerrainSystem;
using StrideTerrain.TerrainSystem.Effects.Material;

namespace StrideTerrain.Sample.Player;

public class PlayerInput : SyncScript
{
    /// <summary>
    /// Raised every frame with the intended direction of movement from the player.
    /// </summary>
    // TODO Should not be static, but allow binding between player and controller
    public static readonly EventKey<Vector3> MoveDirectionEventKey = new EventKey<Vector3>();

    public static readonly EventKey<Vector2> CameraDirectionEventKey = new EventKey<Vector2>();

    public static readonly EventKey<bool> JumpEventKey = new EventKey<bool>();
    private bool jumpButtonDown = false;

    public float DeadZone { get; set; } = 0.25f;

    public CameraComponent Camera { get; set; }

    /// <summary>
    /// Multiplies move movement by this amount to apply aim rotations
    /// </summary>
    public float MouseSensitivity = 100.0f;

    public List<Keys> KeysLeft { get; } = new List<Keys>();

    public List<Keys> KeysRight { get; } = new List<Keys>();

    public List<Keys> KeysUp { get; } = new List<Keys>();

    public List<Keys> KeysDown { get; } = new List<Keys>();

    public List<Keys> KeysJump { get; } = new List<Keys>();

    public override void Update()
    {
        // Character movement: should be camera-aware
        {
            // Left stick: movement
            var moveDirection = Vector2.Zero;

            // Keyboard: movement
            if (KeysLeft.Any(key => Input.IsKeyDown(key)))
                moveDirection += -Vector2.UnitX;
            if (KeysRight.Any(key => Input.IsKeyDown(key)))
                moveDirection += +Vector2.UnitX;
            if (KeysUp.Any(key => Input.IsKeyDown(key)))
                moveDirection += +Vector2.UnitY;
            if (KeysDown.Any(key => Input.IsKeyDown(key)))
                moveDirection += -Vector2.UnitY;

            // Broadcast the movement vector as a world-space Vector3 to allow characters to be controlled
            var worldSpeed = Camera != null
                ? Utils.LogicDirectionToWorldDirection(moveDirection, Camera, Vector3.UnitY)
                : new Vector3(moveDirection.X, 0, moveDirection.Y);

            // Adjust vector's magnitute - worldSpeed has been normalized
            var moveLength = moveDirection.Length();
            var isDeadZoneLeft = moveLength < DeadZone;
            if (isDeadZoneLeft)
            {
                worldSpeed = Vector3.Zero;
            }
            else
            {
                if (moveLength > 1)
                {
                    moveLength = 1;
                }
                else
                {
                    moveLength = (moveLength - DeadZone) / (1f - DeadZone);
                }

                worldSpeed *= moveLength;
            }

            MoveDirectionEventKey.Broadcast(worldSpeed);
        }

        // Camera rotation: left-right rotates the camera horizontally while up-down controls its altitude
        {
            // Right stick: camera rotation
            var cameraDirection = Vector2.Zero;
            var isDeadZoneRight = cameraDirection.Length() < DeadZone;
            if (isDeadZoneRight)
                cameraDirection = Vector2.Zero;
            else
                cameraDirection.Normalize();

            // Mouse-based camera rotation. Only enabled after you click the screen to lock your cursor, pressing escape cancels this
            if (Input.IsMouseButtonDown(MouseButton.Left) && !ImGui.GetIO().WantCaptureMouse)
            {
                Input.LockMousePosition(true);
                Game.IsMouseVisible = false;
            }
            if (Input.IsKeyPressed(Keys.Escape))
            {
                Input.UnlockMousePosition();
                Game.IsMouseVisible = true;
            }
            if (Input.IsMousePositionLocked)
            {
                cameraDirection += new Vector2(Input.MouseDelta.X, -Input.MouseDelta.Y) * MouseSensitivity;
            }

            // Broadcast the camera direction directly, as a screen-space Vector2
            CameraDirectionEventKey.Broadcast(cameraDirection);
        }

        // Jumping: don't bother with jump restrictions here, just pass the button states
        {
            // Keyboard: jumping
            var didJump = KeysJump.Any(key => Input.IsKeyPressed(key));

            JumpEventKey.Broadcast(didJump);
        }

        if (Input.IsKeyPressed(Keys.G))
        {
            var simulation = this.GetSimulation();
            var p = Entity.Transform.Position;
            var result = simulation.Raycast(new Vector3(p.X, 1000, p.Z), new Vector3(p.X, -1000, p.Z));
            
            if (result.Succeeded && result.Collider.Entity != Entity)
            {
                Entity.Get<CharacterComponent>().Teleport(result.Point);
            }
        }
    }
}
