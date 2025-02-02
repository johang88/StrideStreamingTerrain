using Stride.Rendering;

namespace StrideTerrain.Weather;

public class WeatherRenderStageSelector : RenderStageSelector
{
    public RenderStage? Opaque { get; set; }
    public RenderStage? Transparent { get; set; }

    public string EffectName { get; set; } = "AtmosphereRenderSkyEffect";

    public override void Process(RenderObject renderObject)
    {
        if (Opaque != null)
        {
            renderObject.ActiveRenderStages[Opaque.Index] = new ActiveRenderStage(EffectName);
        }

        if (Transparent != null)
        {
            renderObject.ActiveRenderStages[Transparent.Index] = new ActiveRenderStage(EffectName);
        }
    }
}
