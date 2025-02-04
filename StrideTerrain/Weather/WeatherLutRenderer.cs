using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Graphics;
using StrideTerrain.Weather.Effects.Atmosphere.LUT;
using Stride.Core.Mathematics;
using Stride.Core;

namespace StrideTerrain.Weather;

public class WeatherLutRenderer : SceneRendererBase
{
    public static readonly PropertyKey<Texture> TransmittanceLut = new("WeatherLutRenderer.TransmittanceLut", typeof(WeatherRenderObject));
    public static readonly PropertyKey<Texture> MultiScatteredLuminanceLut = new("WeatherLutRenderer.MultiScatteredLuminanceLut", typeof(WeatherRenderObject));
    public static readonly PropertyKey<Texture> SkyLuminanceLut = new("WeatherLutRenderer.SkyLuminanceLut", typeof(WeatherRenderObject));
    public static readonly PropertyKey<Texture> SkyViewLut = new("WeatherLutRenderer.SkyViewLut", typeof(WeatherRenderObject));

    private LookupTextureEffect? _transmittanceLut;
    private LookupTextureEffect? _mulitScatteredLuminanceLut;
    private LookupTextureEffect? _skyLuminanceLut;
    private LookupTextureEffect? _skyViewLut;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _transmittanceLut = new(this, Context, "AtmosphereTransmittanceLut", 256, 64, 1, PixelFormat.R16G16B16A16_Float);
        _mulitScatteredLuminanceLut = new(this, Context, "AtmosphereMultiScatteredLuminanceLut", 32, 32, 1, PixelFormat.R16G16B16A16_Float);
        _skyLuminanceLut = new(this, Context, "AtmosphereSkyLuminanceLut", 1, 1, 1, PixelFormat.R16G16B16A16_Float);
        _skyViewLut = new(this, Context, "AtmosphereSkyViewLut", 192, 104, 1, PixelFormat.R16G16B16A16_Float);
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        if (!context.Tags.TryGetValue(WeatherRenderObject.Current, out var weather))
        {
            context.Tags.Remove(TransmittanceLut);
            context.Tags.Remove(MultiScatteredLuminanceLut);
            context.Tags.Remove(SkyLuminanceLut);
            context.Tags.Remove(SkyViewLut);
            return;
        }

        if (_transmittanceLut == null || _mulitScatteredLuminanceLut == null || _skyLuminanceLut == null || _skyViewLut == null)
            return;

        var atmosphere = weather.Atmosphere;
        var sunDirection = weather.SunDirection;
        var sunColor = weather.SunColor;

        var inverseViewMatrix = Matrix.Invert(context.RenderView.View);
        var eye = inverseViewMatrix.Row4;
        var cameraPosition = new Vector3(eye.X, eye.Y, eye.Z);

        RenderTransmittanceLut(drawContext, atmosphere, sunDirection, sunColor);
        RenderMulitScatteredLuminanceLut(drawContext, atmosphere);
        RenderSkyLuminanceLut(drawContext, atmosphere, sunDirection, sunColor);
        RenderSkyViewLut(drawContext, atmosphere, cameraPosition, sunDirection, sunColor);

        context.Tags.Set(TransmittanceLut, _transmittanceLut.Texture);
        context.Tags.Set(MultiScatteredLuminanceLut, _mulitScatteredLuminanceLut.Texture);
        context.Tags.Set(SkyLuminanceLut, _skyLuminanceLut.Texture);
        context.Tags.Set(SkyViewLut, _skyViewLut.Texture);
    }

    void RenderTransmittanceLut(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 sunDirection, Color3 sunColor)
    {
        if (_transmittanceLut == null)
            return;

        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.OutputTexture, _transmittanceLut.Texture);
        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.Atmosphere, atmosphere);
        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.SunDirection, sunDirection);
        _transmittanceLut.Effect.Parameters.Set(AtmosphereTransmittanceLutKeys.SunColor, sunColor);

        _transmittanceLut.Effect.ThreadNumbers = new(8, 8, 1);
        _transmittanceLut.Effect.ThreadGroupCounts = new(_transmittanceLut.Texture.Width / 8, _transmittanceLut.Texture.Height / 8, 1);
        _transmittanceLut.Effect.Draw(context, "Atmosphere.LUT.Transmittance");
    }

    void RenderMulitScatteredLuminanceLut(RenderDrawContext context, AtmosphereParameters atmosphere)
    {
        if (_transmittanceLut == null || _mulitScatteredLuminanceLut == null)
            return;

        _mulitScatteredLuminanceLut.Effect.Parameters.Set(AtmosphereMultiScatteredLuminanceLutKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _mulitScatteredLuminanceLut.Effect.Parameters.Set(AtmosphereMultiScatteredLuminanceLutKeys.OutputTexture, _mulitScatteredLuminanceLut.Texture);
        _mulitScatteredLuminanceLut.Effect.Parameters.Set(AtmosphereMultiScatteredLuminanceLutKeys.Atmosphere, atmosphere);

        _mulitScatteredLuminanceLut.Effect.ThreadNumbers = new(1, 1, 64);
        _mulitScatteredLuminanceLut.Effect.ThreadGroupCounts = new(_mulitScatteredLuminanceLut.Texture.Width, _mulitScatteredLuminanceLut.Texture.Height, 1);
        _mulitScatteredLuminanceLut.Effect.Draw(context, "Atmosphere.LUT.MultiScatteredLuminance");
    }

    void RenderSkyLuminanceLut(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 sunDirection, Color3 sunColor)
    {
        if (_transmittanceLut == null || _mulitScatteredLuminanceLut == null || _skyLuminanceLut == null)
            return;

        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.MultiScatteringLUT, _mulitScatteredLuminanceLut.Texture);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.OutputTexture, _skyLuminanceLut.Texture);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.Atmosphere, atmosphere);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.SunDirection, sunDirection);
        _skyLuminanceLut.Effect.Parameters.Set(AtmosphereSkyLuminanceLutKeys.SunColor, sunColor);

        _skyLuminanceLut.Effect.ThreadNumbers = new(1, 1, 64);
        _skyLuminanceLut.Effect.ThreadGroupCounts = new(_skyLuminanceLut.Texture.Width, _skyLuminanceLut.Texture.Height, 1);
        _skyLuminanceLut.Effect.Draw(context, "Atmosphere.LUT.SkyLuminance");
    }

    void RenderSkyViewLut(RenderDrawContext context, AtmosphereParameters atmosphere, Vector3 cameraPosition, Vector3 sunDirection, Color3 sunColor)
    {
        if (_skyViewLut == null || _transmittanceLut == null || _mulitScatteredLuminanceLut == null)
            return;

        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.TransmittanceLUT, _transmittanceLut.Texture);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.MultiScatteringLUT, _mulitScatteredLuminanceLut.Texture);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.OutputTexture, _skyViewLut.Texture);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.Atmosphere, atmosphere);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.SunDirection, sunDirection);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.SunColor, sunColor);
        _skyViewLut.Effect.Parameters.Set(AtmosphereSkyViewLutKeys.CameraPosition, cameraPosition);

        _skyViewLut.Effect.ThreadNumbers = new(8, 8, 1);
        _skyViewLut.Effect.ThreadGroupCounts = new(_skyViewLut.Texture.Width / 8, _skyViewLut.Texture.Height / 8, 1);
        _skyViewLut.Effect.Draw(context, "Atmosphere.LUT.SkyViewLut");
    }
}
