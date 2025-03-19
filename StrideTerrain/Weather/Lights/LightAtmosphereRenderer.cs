using Stride.Core.Collections;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Skyboxes;
using Stride.Shaders;
using StrideTerrain.Rendering;
using StrideTerrain.Weather.Effects.Lights;
using System;
using System.Collections.Generic;
namespace StrideTerrain.Weather.Lights;

public class LightAtmosphereRenderer : LightGroupRendererBase
{
    private readonly Dictionary<RenderLight, LightSkyBoxShaderGroup> lightShaderGroupsPerSkybox = new Dictionary<RenderLight, LightSkyBoxShaderGroup>();
    private PoolListStruct<LightSkyBoxShaderGroup> pool = new PoolListStruct<LightSkyBoxShaderGroup>(8, CreateLightSkyBoxShaderGroup);

    public override Type[] LightTypes { get; } = { typeof(LightAtmosphere) };

    public LightAtmosphereRenderer()
    {
        IsEnvironmentLight = true;
    }

    /// <param name="viewCount"></param>
    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();

        foreach (var lightShaderGroup in lightShaderGroupsPerSkybox)
            lightShaderGroup.Value.Reset();

        lightShaderGroupsPerSkybox.Clear();
        pool.Reset();
    }

    /// <inheritdoc/>
    public override void ProcessLights(ProcessLightsParameters parameters)
    {
        foreach (var index in parameters.LightIndices)
        {
            // For now, we allow only one cubemap at once
            var light = parameters.LightCollection[index];

            // Prepare LightSkyBoxShaderGroup
            LightSkyBoxShaderGroup? lightShaderGroup;
            if (!lightShaderGroupsPerSkybox.TryGetValue(light, out lightShaderGroup))
            {
                lightShaderGroup = pool.Add();
                lightShaderGroup.Light = light;

                lightShaderGroupsPerSkybox.Add(light, lightShaderGroup);
            }
        }

        // Consume all the lights
        parameters.LightIndices.Clear();
    }

    public override void UpdateShaderPermutationEntry(ForwardLightingRenderFeature.LightShaderPermutationEntry shaderEntry)
    {
        foreach (var cubemap in lightShaderGroupsPerSkybox)
        {
            shaderEntry.EnvironmentLights.Add(cubemap.Value);
        }
    }

    private static LightSkyBoxShaderGroup CreateLightSkyBoxShaderGroup()
    {
        return new LightSkyBoxShaderGroup(new ShaderMixinGeneratorSource("AtmosphereCubeMapEnvironmentColor"));
    }

    private class LightSkyBoxShaderGroup : LightShaderGroup
    {
        private ValueParameterKey<float> _intensityKey = null!;
        private ObjectParameterKey<Texture> _cubeMapKey = null!;
        private ValueParameterKey<float> _mipCountKey = null!;
        private ValueParameterKey<bool> _isRenderingCubeMap = null!;

        public RenderLight Light { get; set; } = null!;

        public LightSkyBoxShaderGroup(ShaderSource mixin) : base(mixin)
        {
            HasEffectPermutations = true;
        }

        public override void UpdateLayout(string compositionName)
        {
            base.UpdateLayout(compositionName);

            _intensityKey = AtmosphereCubeMapEnvironmentColorKeys.Intensity.ComposeWith(compositionName);
            _cubeMapKey = AtmosphereCubeMapEnvironmentColorKeys.CubeMap.ComposeWith(compositionName);
            _isRenderingCubeMap = AtmosphereCubeMapEnvironmentColorKeys.IsRenderingCubeMap.ComposeWith(compositionName);
            _mipCountKey = AtmosphereCubeMapEnvironmentColorKeys.MipCount.ComposeWith(compositionName);
        }

        public override void ApplyViewParameters(RenderDrawContext context, int viewIndex, ParameterCollection parameters)
        {
            base.ApplyViewParameters(context, viewIndex, parameters);

            var lightSkybox = (LightAtmosphere)Light.Type;
            var skybox = lightSkybox.DynamicSkyBox;

            if (skybox == null)
                return;

            var intensity = Light.Intensity;

            var specularParameters = skybox.SpecularLightingParameters;

            var specularCubemap = specularParameters.Get(SkyboxKeys.CubeMap);
            int specularCubemapLevels = 0;
            if (specularCubemap != null)
            {
                specularCubemapLevels = specularCubemap.MipLevels;
            }

            context.RenderContext.Tags.TryGetValue(CubeMapRenderer.IsRenderingCubemap, out var isRenderingCubeMap);

            parameters.Set(_intensityKey, intensity);
            parameters.Set(_cubeMapKey, specularCubemap!);
            parameters.Set(_mipCountKey, specularCubemapLevels);
            parameters.Set(_isRenderingCubeMap, isRenderingCubeMap);
        }
    }
}
