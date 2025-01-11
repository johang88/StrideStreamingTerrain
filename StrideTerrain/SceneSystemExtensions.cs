using Stride.Engine;

namespace StrideTerrain;

public static class SceneSystemExtensions
{
    /// <summary>
    /// Try to get the main camera in a game, this is mainly used
    /// to prevent linking directly with a camera component and to locate
    /// the camera in the editor scene.
    /// 
    /// Proper usage should use render features instead of this though where you get 
    /// the camera information through the render view instead. This does makes it simpler and 
    /// also allows to make some easier optimizations as no logic to reduce redundant calcualtions over mulitple view (shadow casting etc)
    /// is neeed. 
    /// </summary>
    /// <param name="sceneSystem"></param>
    /// <returns></returns>
    public static CameraComponent? TryGetMainCamera(this SceneSystem sceneSystem)
    {
        CameraComponent? camera = null;
        if (sceneSystem.GraphicsCompositor.Cameras.Count == 0)
        {
            // The compositor wont have any cameras attached if the game is running in editor mode
            // Search through the scene systems until the camera entity is found
            // This is what you might call "A Hack"
            foreach (var system in sceneSystem.Game.GameSystems)
            {
                if (system is SceneSystem editorSceneSystem)
                {
                    foreach (var entity in editorSceneSystem.SceneInstance.RootScene.Entities)
                    {
                        if (entity.Name == "Camera Editor Entity")
                        {
                            camera = entity.Get<CameraComponent>();
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            camera = sceneSystem.GraphicsCompositor.Cameras[0].Camera;
        }

        return camera;
    }
}
