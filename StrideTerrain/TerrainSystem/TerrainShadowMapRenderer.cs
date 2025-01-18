using SharpFont;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Physics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using StrideTerrain.TerrainSystem.Effects;
using System;

namespace StrideTerrain.TerrainSystem;

/// <summary>
/// Renders a custom shadow map for the terrain as well as all regular shadow maps.
/// 
/// NOTE: Requires changing Draw() to virtual in ShadowMapRenderer. Could also implement 
/// IShadowMapRenderer and forward to the regular shadow map renderer but I'm too lazy to do that.
/// 
/// Or maybe add a fancy `CompoundShadowMapRenderer` that has a list of shadow map renderer!
/// </summary>
[DataContract(DefaultMemberMode = DataMemberMode.Never)]
public class TerrainShadowMapRenderer : ShadowMapRenderer
{
    private static ValueParameterKey<Int4> StartXYKey = ParameterKeys.NewValue(Int4.Zero, "TerrainShadowGenerator.StartXY");

    private ComputeEffectShader? _terrainShadowGeneratorEffect;
    private ComputeEffectShader? _terrainGenereateHeightmapEffect;
    private Texture? _terrainHeightmap;

    internal Int4[] StartsBuffer = new Int4[4096];
    internal PerGroupDataStruct[] PerGroupDataBuffer = new PerGroupDataStruct[4096];

    private Vector3? _previousLightDirection = null;

    public override void Draw(RenderDrawContext drawContext)
    {
        if (drawContext.Tags.TryGetValue(TerrainRenderFeature.TerrainList, out var terrains) && terrains.Count == 1)
        {
            var terrain = terrains[0];
            DrawTerrainShadowMap(drawContext, terrain);
        }

        base.Draw(drawContext);
    }

    private void DrawTerrainShadowMap(RenderDrawContext drawContext, TerrainRuntimeData terrain)
    {
        if (!terrain.IsInitialized || terrain.ShadowMap == null)
            return;

        var renderSystem = drawContext.RenderContext.RenderSystem;
        var renderContext = drawContext.RenderContext;

        var lights = renderContext.VisibilityGroup.Tags.Get(ForwardLightingRenderFeature.CurrentLights);
        if (lights == null)
            return;

        RenderLight? directionalLight = null;
        foreach (var light in lights)
        {
            if (light.Type is LightDirectional lightDirectional && lightDirectional.Shadow != null && lightDirectional.Shadow.Enabled)
            {
                directionalLight = light;
                break;
            }
        }

        if (directionalLight == null)
            return;

        if ( _terrainShadowGeneratorEffect == null)
        {
            _terrainShadowGeneratorEffect = new ComputeEffectShader(drawContext.RenderContext) { ShaderSourceName = "TerrainShadowGenerator" };
            _terrainShadowGeneratorEffect.DisposeBy(drawContext);
        }

        if (_terrainGenereateHeightmapEffect == null)
        {
            _terrainGenereateHeightmapEffect = new ComputeEffectShader(drawContext.RenderContext) { ShaderSourceName = "TerrainGenerateHeightmap" };
            _terrainGenereateHeightmapEffect.DisposeBy(drawContext);
        }

        if (_terrainHeightmap == null)
        {
            _terrainHeightmap = Texture.New2D(renderContext.GraphicsDevice, TerrainRuntimeData.ShadowMapSize, TerrainRuntimeData.ShadowMapSize, PixelFormat.R16_Float, TextureFlags.UnorderedAccess | TextureFlags.ShaderResource);
            _terrainHeightmap.DisposeBy(drawContext);
        }

        var lightDirection = directionalLight.Direction;
        lightDirection.Normalize();

        var lightCosAngleChange = 0.0f;
        if (_previousLightDirection != null)
        {
            lightCosAngleChange = Vector3.Dot(_previousLightDirection.Value, lightDirection);
            lightCosAngleChange = MathUtil.Clamp(lightCosAngleChange, -1.0f, 1.0f);
        }

        //if (lightCosAngleChange < 0.9f)
        {
            _previousLightDirection = lightDirection;
            var shadowMapToTerrainSize = terrain.TerrainData.Header.Size / TerrainRuntimeData.ShadowMapSize;

            // Build height map, currently not used ... 
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainGenerateHeightmapKeys.OutputTexture, _terrainHeightmap);
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainGenerateHeightmapKeys.OutputSizeToTerrainSize, (uint)shadowMapToTerrainSize);
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainGenerateHeightmapKeys.OutputSize, (uint)TerrainShadowMapResolution);
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainCommonKeys.Heightmap, terrain.HeightmapTexture);
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainCommonKeys.SectorToChunkMapBuffer, terrain.SectorToChunkMapBuffer);
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainCommonKeys.ChunkBuffer, terrain.ChunkBuffer);
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainCommonKeys.ChunkSize, (uint)terrain.TerrainData.Header.ChunkSize);
            //_terrainGenereateHeightmapEffect.Parameters.Set(TerrainCommonKeys.ChunksPerRow, (uint)terrain.ChunksPerRowLod0);

            //_terrainGenereateHeightmapEffect.ThreadGroupCounts = new Int3(64, 64, 1);
            //_terrainGenereateHeightmapEffect.ThreadNumbers = new Int3(32, 32, 1);

            //_terrainGenereateHeightmapEffect.Draw(drawContext, "ShadowMapRenderer.GenerateHeightmap");

            // TODO: Only recalculate if light has moved.

            //var xzDimensions = new Vector2(TerrainShadowMapResolution, TerrainShadowMapResolution);
            var xzDimensions = new Vector2(terrain.TerrainData.Header.Size * terrain.UnitsPerTexel, terrain.TerrainData.Header.Size * terrain.UnitsPerTexel);

            var lightDir2d = new Vector2(lightDirection.X, lightDirection.Z);
            lightDir2d.Normalize();

            var heightDelta = lightDirection.Y;

            if (lightDir2d.LengthSquared() < 1e-6f)
            {
                //lightDir = Vector3::UNIT_Y. Fix NaNs.
                lightDir2d.X = 1.0f;
                lightDir2d.Y = 0.0f;
            }

            var width = (uint)terrain.ShadowMap.Width;
            var height = (uint)terrain.ShadowMap.Height;

            //Bresenham's line algorithm.
            var x0 = 0.0f;
            var y0 = 0.0f;
            var x1 = (float)(width - 1u);
            var y1 = (float)(height - 1u);

            uint heightOrWidth;
            uint widthOrHeight;

            if (Math.Abs(lightDir2d.X) > Math.Abs(lightDir2d.Y))
            {
                y1 *= Math.Abs(lightDir2d.Y) / Math.Abs(lightDir2d.X);
                heightOrWidth = height;
                widthOrHeight = width;

                heightDelta *= 1.0f / Math.Abs(lightDirection.X);
            }
            else
            {
                x1 *= Math.Abs(lightDir2d.X) / Math.Abs(lightDir2d.Y);
                heightOrWidth = width;
                widthOrHeight = height;

                heightDelta *= 1.0f / Math.Abs(lightDirection.Z);
            }

            if (lightDir2d.X < 0)
                Swap(ref x0, ref x1);
            if (lightDir2d.Y < 0)
                Swap(ref y0, ref y1);

            var steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
            if (steep)
            {
                Swap(ref x0, ref y0);
                Swap(ref x1, ref y1);
            }

            _terrainShadowGeneratorEffect.Parameters.Set(TerrainShadowGeneratorKeys.IsSteep, steep ? 1 : 0);

            float dx;
            float dy;
            {
                float _x0 = x0;
                float _y0 = y0;
                float _x1 = x1;
                float _y1 = y1;
                if (_x0 > _x1)
                {
                    Swap(ref _x0, ref _x1);
                    Swap(ref _y0, ref _y1);
                }
                dx = _x1 - _x0 + 1.0f;
                dy = Math.Abs(_y1 - _y0);
                if (Math.Abs(lightDir2d.X) > Math.Abs(lightDir2d.Y))
                    dy += 1.0f * Math.Abs(lightDir2d.Y) / Math.Abs(lightDir2d.X);
                else
                    dy += 1.0f * Math.Abs(lightDir2d.X) / Math.Abs(lightDir2d.Y);

                _terrainShadowGeneratorEffect.Parameters.Set(TerrainShadowGeneratorKeys.Delta, new Vector2(dx, dy));
            }

            var xyStep = new Int2((x0 < x1) ? 1 : -1, (y0 < y1) ? 1 : -1);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainShadowGeneratorKeys.XyStep, new Vector2(xyStep.X, xyStep.Y));

            heightDelta = (-heightDelta * (xzDimensions.X / width)) / terrain.TerrainData.Header.MaxHeight;
            //Avoid sending +/- inf (which causes NaNs inside the shader).
            //Values greater than 1.0 (or less than -1.0) are pointless anyway.
            heightDelta = Math.Max(-1.0f, Math.Min(1.0f, heightDelta));

            _terrainShadowGeneratorEffect.Parameters.Set(TerrainShadowGeneratorKeys.HeightDelta, heightDelta);

            //y0 is not needed anymore, and we need it to be either 0 or heightOrWidth for the
            //algorithm to work correctly (depending on the sign of xyStep[1]). So do this now.
            if (y0 >= y1)
                y0 = heightOrWidth;

            float fStep = (dx * 0.5f) / dy;
            //TODO numExtraIterations correct? -1? +1?
            uint numExtraIterations = (uint)(Math.Min(Math.Ceiling(dy), Math.Ceiling(((heightOrWidth - 1u) / fStep - 1u) * 0.5f)));

            uint threadsPerGroup = 64;
            uint firstThreadGroups = AlignToNextMultiple(heightOrWidth, threadsPerGroup) / threadsPerGroup;
            uint lastThreadGroups = AlignToNextMultiple(numExtraIterations, threadsPerGroup) / threadsPerGroup;
            uint totalThreadGroups = firstThreadGroups + lastThreadGroups;

            var idy = (int)Math.Floor(dy);

            //"First" series of threadgroups
            var startsIndex = 0u;
            var writeXY = true;
            for (var h = 0; h < firstThreadGroups; ++h)
            {
                var startY = h * threadsPerGroup;

                for (var i = 0; i < threadsPerGroup; ++i)
                {
                    if (writeXY)
                    {
                        StartsBuffer[startsIndex].X = (int)x0;
                        StartsBuffer[startsIndex].Y = (int)y0 + (int)((startY + i) * xyStep.Y);
                    }
                    else
                    {
                        StartsBuffer[startsIndex].Z = (int)x0;
                        StartsBuffer[startsIndex].W = (int)y0 + (int)((startY + i) * xyStep.Y);
                    }

                    startsIndex++;
                    if (startsIndex >= 4096)
                    {
                        startsIndex -= 4096;
                        writeXY = false;
                    }
                }

                PerGroupDataBuffer[h].Iterations = (int)widthOrHeight - Math.Max(0, idy - (int)(heightOrWidth - startY));
                PerGroupDataBuffer[h].DeltaErrorStart = 0;
            }

            //"Last" series of threadgroups
            for (var h = 0; h < lastThreadGroups; ++h)
            {
                var xN = GetXStepsNeededToReachY(threadsPerGroup * (uint)h + 1u, fStep);

                for (var i = 0; i < threadsPerGroup; ++i)
                {
                    if (writeXY)
                    {
                        StartsBuffer[startsIndex].X = (int)x0 + (xN * xyStep.X);
                        StartsBuffer[startsIndex].Y = (int)y0 - (i * xyStep.Y);
                    }
                    else
                    {
                        StartsBuffer[startsIndex].Z = (int)x0 + (xN * xyStep.X);
                        StartsBuffer[startsIndex].W = (int)y0 - (i * xyStep.Y);
                    }

                    startsIndex++;
                    if (startsIndex >= 4096)
                    {
                        startsIndex -= 4096;
                        writeXY = false;
                    }
                }

                PerGroupDataBuffer[firstThreadGroups + h].Iterations = (int)widthOrHeight - xN;
                PerGroupDataBuffer[firstThreadGroups + h].DeltaErrorStart = GetErrorAfterXsteps((uint)xN, dx, dy) - dx * 0.5f;
            }

            // Set parameters and dispatch
            _terrainShadowGeneratorEffect.Parameters.Set(StartXYKey, StartsBuffer.Length, ref StartsBuffer[0]);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainShadowGeneratorKeys.PerGroupData, PerGroupDataBuffer.Length, ref PerGroupDataBuffer[0]);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainShadowGeneratorKeys.ShadowMap, terrain.ShadowMap);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainShadowGeneratorKeys.ShadowMapToTerrainSize, (uint)shadowMapToTerrainSize);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainCommonKeys.Heightmap, terrain.HeightmapTexture);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainCommonKeys.SectorToChunkMapBuffer, terrain.SectorToChunkMapBuffer);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainCommonKeys.ChunkBuffer, terrain.ChunkBuffer);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainCommonKeys.ChunkSize, (uint)terrain.TerrainData.Header.ChunkSize);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainCommonKeys.ChunksPerRow, (uint)terrain.ChunksPerRowLod0);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainCommonKeys.MaxHeight, terrain.TerrainData.Header.MaxHeight);
            _terrainShadowGeneratorEffect.Parameters.Set(TerrainCommonKeys.InvTerrainTextureSize, TerrainRuntimeData.InvRuntimeTextureSize);

            _terrainShadowGeneratorEffect.ThreadGroupCounts = new Int3((int)totalThreadGroups, 1, 1);
            _terrainShadowGeneratorEffect.ThreadNumbers = new Int3((int)threadsPerGroup, 1, 1);

            _terrainShadowGeneratorEffect.Draw(drawContext, "ShadowMapRenderer.Terrain");
        }
    }

    static int GetXStepsNeededToReachY(uint y, float fStep)
    {
        return (int)(Math.Ceiling(Math.Max(((y << 1) - 1u) * fStep, 0.0f)));
    }

    static float GetErrorAfterXsteps(uint xIterationsToSkip, float dx, float dy)
    {
        //Round accumulatedError to next multiple of dx, then subtract accumulatedError.
        //That's the error at position (x; y). *MUST* be done in double precision, otherwise
        //we get artifacts with certain light angles.
        double accumulatedError = dx * 0.5 + dy * (double)(xIterationsToSkip);
        double newErrorAtX = Math.Ceiling(accumulatedError / dx) * dx - accumulatedError;
        return (float)(newErrorAtX);
    }

    static uint AlignToNextMultiple(uint offset, uint alignment)
    {
        return ((offset + alignment - 1u) / alignment) * alignment;
    }

    static void Swap<T>(ref T a, ref T b)
    {
        T tmp = b;
        b = a;
        a = tmp;
    }
}
