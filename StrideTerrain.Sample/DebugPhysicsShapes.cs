﻿using System.Linq;
using System.Threading.Tasks;
using Stride.Input;
using Stride.Engine;
using Stride.Physics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using StrideTerrain.Rendering;
using StrideTerrain.Rendering.ReverseZ;

namespace StrideTerrain.Sample
{
    public class DebugPhysicsShapes : AsyncScript
    {
        public RenderGroup RenderGroup = RenderGroup.Group31;

        public override async Task Execute()
        {
            // Setup rendering in the debug entry point if we have it
            var compositor = SceneSystem.GraphicsCompositor;
            var debugRenderer =
                ((compositor.Game as SceneCameraRenderer)?.Child as SceneRendererCollection)?.Children.Where(
                    x => x is DebugRenderer).Cast<DebugRenderer>().FirstOrDefault();
            if (debugRenderer == null)
                return;

            var shapesRenderState = new RenderStage("PhysicsDebugShapes", "Main");
            compositor.RenderStages.Add(shapesRenderState);
            var meshRenderFeature = compositor.RenderFeatures.OfType<MeshRenderFeature>().First();
            meshRenderFeature.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
            {
                EffectName = "StrideForwardShadingEffect",
                RenderGroup = (RenderGroupMask)(1 << (int)RenderGroup),
                RenderStage = shapesRenderState,
            });
            meshRenderFeature.PipelineProcessors.Add(new CustomWireframePipelineProcessor { RenderStage = shapesRenderState });
            debugRenderer.DebugRenderStages.Add(shapesRenderState);

            var simulation = this.GetSimulation();
            if (simulation != null)
                simulation.ColliderShapesRenderGroup = RenderGroup;

            var enabled = false;
            while (Game.IsRunning)
            {
                if (Input.IsKeyDown(Keys.LeftShift) && Input.IsKeyDown(Keys.LeftCtrl) && Input.IsKeyReleased(Keys.O))
                {
                    if (simulation == null)
                    {
                        simulation = this.GetSimulation();
                        if (simulation != null)
                            simulation.ColliderShapesRenderGroup = RenderGroup;
                    }

                    if (simulation != null)
                    {
                        if (enabled)
                        {
                            simulation.ColliderShapesRendering = false;
                            enabled = false;
                        }
                        else
                        {
                            simulation.ColliderShapesRendering = true;
                            enabled = true;
                        }
                    }
                }

                await Script.NextFrame();
            }
        }
    }
}
