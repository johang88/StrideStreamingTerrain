using Stride.Core.Mathematics;
using Stride.Engine;

namespace StrideTerrain;

public static class CameraExtensions
{
    public static Vector3 GetWorldPosition(this CameraComponent camera)
    {
        var viewMatrix = camera.ViewMatrix;
        viewMatrix.Invert();

        var cameraPosition = viewMatrix.TranslationVector;

        return cameraPosition;
    }
}