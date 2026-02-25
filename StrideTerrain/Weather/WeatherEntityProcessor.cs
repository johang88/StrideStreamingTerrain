using Stride.Animations;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using StrideTerrain.Rendering;

namespace StrideTerrain.Weather;

public class WeatherEntityProcessor : EntityProcessor<WeatherComponent, WeatherRenderObject>, IEntityComponentRenderProcessor
{
    public VisibilityGroup VisibilityGroup { get; set; } = null!;
    private WeatherComponent? _activeAtmosphere;

    protected override WeatherRenderObject GenerateComponentData(Entity entity, WeatherComponent component)
         => new() { RenderGroup = RenderGroup.Group30 };

    protected override bool IsAssociatedDataValid(Entity entity, WeatherComponent component, WeatherRenderObject associatedData)
        => true;

    protected override void OnEntityComponentRemoved(Entity entity, WeatherComponent component, WeatherRenderObject data)
    {
        base.OnEntityComponentRemoved(entity, component, data);

        VisibilityGroup.RenderObjects.Remove(data);
        if (_activeAtmosphere == component)
        {
            _activeAtmosphere = null;
        }
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        if (_activeAtmosphere != null)
        {
            var renderObject = ComponentDatas[_activeAtmosphere];
            if (_activeAtmosphere.Sun != null)
            {
                var timeOfDay = _activeAtmosphere.Entity.Get<TimeOfDayComponent>();
                if (timeOfDay != null)
                {
                    _activeAtmosphere.Sun.Entity.Transform.Rotation =
                        Quaternion.RotationX(MathUtil.DegreesToRadians(timeOfDay.SunAngle))
                        * Quaternion.RotationY(MathUtil.DegreesToRadians(-90));
                    _activeAtmosphere.Sun.Entity.Transform.UpdateWorldMatrix();

                    if (timeOfDay.ExposureAutoKeyBiasPower != null && CustomPostProcessingEffects.Current != null)
                    {
                        // TODO: Would make more sense to base it on sun direction?
                        timeOfDay.ExposureAutoKeyBiasPower.UpdateChanges();
                        CustomPostProcessingEffects.Current.ExposureSettings.AutoKeyBiasPower = timeOfDay.ExposureAutoKeyBiasPower.Evaluate(timeOfDay.TimeOfDay);
                    }
                }

                var sunDirection = Vector3.TransformNormal(-Vector3.UnitZ, _activeAtmosphere.Sun.Entity.Transform.WorldMatrix);
                sunDirection.Normalize();

                Color3 sunColor = new();
                if (_activeAtmosphere.Sun.Type is IColorLight colorLight)
                    sunColor = colorLight.ComputeColor(ColorSpace.Linear, _activeAtmosphere.Sun.Intensity);

                var lightProcessor = EntityManager.GetProcessor<LightProcessor>();

                renderObject.SunDirection = -sunDirection;
                renderObject.SunColor = sunColor;
                renderObject.Atmosphere = _activeAtmosphere.Atmosphere;
                renderObject.Clouds = _activeAtmosphere.Clouds;
                renderObject.WeatherMap = _activeAtmosphere.WeatherMap;
                renderObject.Fog = _activeAtmosphere.Fog;
                renderObject.Sun = lightProcessor?.GetRenderLight(_activeAtmosphere.Sun);
            }

            return;
        }
        else
        {
            context.Tags.Remove(WeatherRenderObject.Current);
        }

        // Add first enabled atmosphere to render objects as we only support one atmosphere
        var first = false;
        foreach (var pair in ComponentDatas)
        {
            var component = pair.Value;
            if (component.Enabled && !first)
            {
                first = true;

                VisibilityGroup.RenderObjects.Add(pair.Value);
                _activeAtmosphere = pair.Key;

                context.Tags.Set(WeatherRenderObject.Current, pair.Value);
            }
        }
    }
}
