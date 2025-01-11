using Stride.Core.Mathematics;
using Stride.Engine;
using System;
using System.Linq;

namespace StrideTerrain.Sample;

static class Utils
{
    public static Vector3 LogicDirectionToWorldDirection(Vector2 logicDirection, CameraComponent camera, Vector3 upVector)
    {
        var inverseView = Matrix.Invert(camera.ViewMatrix);

        var forward = Vector3.Cross(upVector, inverseView.Right);
        forward.Normalize();

        var right = Vector3.Cross(forward, upVector);
        var worldDirection = forward * logicDirection.Y + right * logicDirection.X;
        worldDirection.Normalize();
        return worldDirection;
    }

    public static void GetScreenPositionToWorldPositionRay(Vector2 screenPos, CameraComponent camera, out Vector3 from, out Vector3 to)
    {
        var invViewProj = Matrix.Invert(camera.ViewProjectionMatrix);

        // Reconstruct the projection-space position in the (-1, +1) range.
        //    Don't forget that Y is down in screen coordinates, but up in projection space
        Vector3 sPos;
        sPos.X = screenPos.X * 2f - 1f;
        sPos.Y = 1f - screenPos.Y * 2f;

        // Compute the near (start) point for the raycast
        // It's assumed to have the same projection space (x,y) coordinates and z = 0 (lying on the near plane)
        // We need to unproject it to world space
        sPos.Z = 0f;
        var vectorNear = Vector3.Transform(sPos, invViewProj);
        vectorNear /= vectorNear.W;

        // Compute the far (end) point for the raycast
        // It's assumed to have the same projection space (x,y) coordinates and z = 1 (lying on the far plane)
        // We need to unproject it to world space
        sPos.Z = 1f;
        var vectorFar = Vector3.Transform(sPos, invViewProj);
        vectorFar /= vectorFar.W;

        from = vectorNear.XYZ();
        to = vectorFar.XYZ();
    }

    public static Entity GetEntityByPath(this Scene scene, params string[] path)
    {
        var entity = scene.Entities.First(e => e.Name == path[0]);
        for (var i = 1; i < path.Length; i++)
        {
            entity = entity.GetChildren().First(e => e.Name == path[i]);
        }

        return entity;
    }
}