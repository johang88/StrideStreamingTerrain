using Stride.Core.Mathematics;
using Stride.Engine;

namespace StrideTerrain.Sample.Player.Cameras;

public class CameraManager : SyncScript
{
    public ICameraController? Controller { get; set; }

    public required CameraComponent Camera { get; set; }

    public override void Update()
    {
        var deltaTime = (float)Game.UpdateTime.TimePerFrame.TotalSeconds;

        if (Controller != null)
        {
            Controller.Update(deltaTime, out var position, out var rotation, out var fov);

            Camera.Entity.Transform.Position = position;
            Camera.Entity.Transform.Rotation = rotation;
            Camera.VerticalFieldOfView = fov;
        }
    }
}

public interface ICameraController
{ 
    void Update(float deltaTime, out Vector3 position, out Quaternion rotation, out float fov);
}