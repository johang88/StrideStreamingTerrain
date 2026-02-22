using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using System;

namespace StrideTerrain.Sample.Player;

public class ThirdPersonCameraController : SyncScript
{
    public TransformComponent? Target { get; set; }

    [DataMemberIgnore] public CameraComponent Camera => CameraEntity.Get<CameraComponent>();

    public float RotationSpeed { get; set; } = 360f;
    public float VerticalSpeed { get; set; } = 65f;

    public float MinVerticalAngle { get; set; } = -20f;
    public float MaxVerticalAngle { get; set; } = 70f;

    [DataMemberIgnore] public Entity CameraEntity => Entity.GetChild(0).GetChild(0);

    private Vector3 cameraRotationXYZ = new(-20, 45, 0);
    private Vector3 targetRotationXYZ = new(-20, 45, 0);

    private Vector3 _cameraInput = Vector3.Zero;

    public bool Enabled { get; set; } = true;

    public void SetTarget(TransformComponent? target)
    {
        Target = target;

        if (Target == null || !Enabled)
            return;

        Entity.Transform.Position = Target.Position;
    }

    public override void Update()
    {
        if (Target == null || !Enabled)
            return;

        Entity.Transform.Position = Target.Position;

        var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;

        targetRotationXYZ.X += _cameraInput.Y * dt * VerticalSpeed;
        targetRotationXYZ.X = Math.Max(targetRotationXYZ.X, -MaxVerticalAngle);
        targetRotationXYZ.X = Math.Min(targetRotationXYZ.X, -MinVerticalAngle);

        targetRotationXYZ.Y -= _cameraInput.X * dt * RotationSpeed;

        cameraRotationXYZ = Vector3.Lerp(cameraRotationXYZ, targetRotationXYZ, 0.15f);
        Entity.Transform.RotationEulerXYZ = new Vector3(MathUtil.DegreesToRadians(cameraRotationXYZ.X), MathUtil.DegreesToRadians(cameraRotationXYZ.Y), 0);

        _cameraInput = Vector3.Zero;
    }

    public void SetInput(Vector3 input)
        => _cameraInput = input;
}