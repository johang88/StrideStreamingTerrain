using Stride.Core.MicroThreading;
using Stride.Core;
using Stride.Engine;
using Stride.Physics;
using System;
using System.Threading;
using Stride.Core.Mathematics;

namespace StrideTerrain.Sample.Actors;

public class ActorController : SyncScript
{
    public bool Enabled { get; set; } = true;

    public required Entity ModelEntity { get; set; }
    public required ActorAnimationController ActorAnimationController { get; set; }
    public required CharacterComponent Character { get; set; }
    public required ActorStatsComponent ActorStats { get; set; }

    private Vector3 _moveDirection = Vector3.Zero;

    /// <summary>
    /// Get the current character state
    /// this cannot be set directly but is instead dervied from the actions executed by/on the character
    /// </summary>
    public ActorState State { get; private set; }

    private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

    public override void Start()
    {
        base.Start();

        Character = Entity.Get<CharacterComponent>();
        ActorStats = Entity.Get<ActorStatsComponent>();

        // Sync rotation
        Character.Orientation = Entity.Transform.Rotation * Quaternion.RotationAxis(Vector3.UnitY, MathUtil.DegreesToRadians(180.0f));
        Character.Teleport(Entity.Transform.Position);
    }

    public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
    {
        Character.Orientation = rotation * Quaternion.RotationAxis(Vector3.UnitY, MathUtil.DegreesToRadians(180.0f));
        Character.Teleport(position);
    }

    public override void Update()
    {
        if (!Enabled)
        {
            _moveDirection = Vector3.Zero;
            Character.SetVelocity(Vector3.Zero);

            State = ActorState.Alive;

            ActorAnimationController.Speed = 0.0f;
            ActorAnimationController.ActorState = State;

            return;
        }

        if (State != ActorState.Dead && !ActorStats.IsAlive)
        {
            State = ActorState.Dead;
            _cancellationTokenSource.Cancel();
        }

        var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;

        switch (State)
        {
            case ActorState.Alive:
                var invRotation = Quaternion.Invert(Entity.Transform.Rotation);
                var moveDirection = Vector3.Transform(_moveDirection, invRotation);

                if (_moveDirection.Length() > 0.001f)
                {
                    var newRotation = (float)Math.Atan2(-moveDirection.Z, moveDirection.X) - MathUtil.PiOverTwo;

                    // TODO: Configure speed maybe ...
                    Character.Orientation *= Quaternion.RotationYawPitchRoll(newRotation * 3.14f * 2.0f * dt, 0, 0);
                }

                Character.SetVelocity(_moveDirection * ActorStats.MovementSpeed);
                ActorAnimationController.Speed = _moveDirection.Length();

                break;
            case ActorState.Dead:
                // Can't do much here now can we ... 
                break;
        }

        ActorAnimationController.ActorState = State;
    }

    public void InflictDamage(Entity inflictor, float damage)
    {
        ActorStats.Health.Current -= (int)damage;
        ActorAnimationController.Hurt = true;
    }

    /// <summary>
    /// </summary>
    /// <param name="newMoveDirection">Desired move direction in world space</param>
    public void SetInput(Vector3 newMoveDirection)
    {
        _moveDirection = newMoveDirection;
    }
}


