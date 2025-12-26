using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using Stride.Shaders;
using StrideTerrain.Editor.BlendModes;
using StrideTerrain.Editor.Effects;
using StrideTerrain.Editor.Layers;
using StrideTerrain.Rendering;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static Hexa.NET.ImGui.ImGui;
using static StrideCommunity.ImGuiDebug.ImGuiExtension;

namespace StrideTerrain.Editor;

public class TerrainLayer
{
    public ITerrainLayerType? Type { get; set; }
    private ITerrainLayerType? _type;

    public IBlendMode BlendMode { get; set; } = new BlendNone();
    private IBlendMode? _blendMode;

    private Texture? _cachedOutput;
    private ComputeEffectShader? _shader;

    public List<TerrainLayer> Children { get; set; } = [];
    public bool IsGroup => Type is GroupLayer;

    public string Name = "";

    public bool Enabled = true;
    private bool _enabled = true;

    public void DrawUi()
    {
        var preview = Type == null ? "- None -" : TypeInfoCache.GetLayerName(Type);

        InputText("Name", ref Name, 99);

        using (UCombo("Type", preview, out var open))
        {
            if (open)
            {
                foreach (var category in TypeInfoCache.LayerTypesByCategory)
                {
                    SeparatorText(category.Key);
                    foreach (var layerType in category.Value)
                    {
                        var selected = layerType.Type == Type?.GetType();
                        if (Selectable(layerType.Display?.Name ?? layerType.Type.Name))
                        {
                            Type = (ITerrainLayerType)Activator.CreateInstance(layerType.Type)!;
                        }
                    }
                }
            }
        }

        var selectedBlendMode = BlendMode;
        preview = TypeInfoCache.GetBlendModeName(selectedBlendMode);
        using (UCombo("Blend Mode", preview, out var open))
        {
            if (open)
            {
                foreach (var blendMode in TypeInfoCache.BlendModes)
                {
                    var selected = blendMode.Type == selectedBlendMode.GetType();
                    if (Selectable(blendMode.Display?.Name ?? blendMode.Type.Name))
                    {
                        BlendMode = (IBlendMode)Activator.CreateInstance(blendMode.Type)!;
                    }
                }
            }
        }

        BlendMode.DrawUi();

        if (!IsGroup)
        {
            Separator();
            Type?.DrawUi();
        }
    }

    public (Texture Output, bool WasDirty) Render(TerrainSettings terrainSettings, RenderDrawContext renderDrawContext, Texture input, bool forceRender, Texture? groupOutput = null)
    {
        if (Type == null)
        {
            return (input, false);
        }
        else if (_cachedOutput == null || Type.IsDirty || BlendMode.IsDirty || terrainSettings.IsDirty || forceRender || _type != Type || _blendMode != BlendMode || Enabled != _enabled)
        {
            if (!IsCachedTextureValid(terrainSettings))
            {
                _cachedOutput?.Dispose();
                _cachedOutput = Texture.New2D(renderDrawContext.GraphicsDevice, terrainSettings.Resolution, terrainSettings.Resolution, EditorGame.TerrainPixelFormat, TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);
            }

            _enabled = Enabled;
            if (!Enabled)
            {
                renderDrawContext.CommandList.Copy(input, _cachedOutput);
                return (input, true);
            }

            _shader ??= new ComputeEffectShader(renderDrawContext.RenderContext)
            {
                ShaderSourceName = "ComputeTerrainLayerEffect"
            };

            if (_type != Type)
            {
                _type = Type;
                _shader.Parameters.Set(ComputeTerrainLayerEffectKeys.Layer, new ShaderClassSource(_type.ShaderName));
            }

            if (_blendMode != BlendMode)
            {
                _blendMode = BlendMode;
                _shader.Parameters.Set(ComputeTerrainLayerEffectKeys.BlendMode, new ShaderClassSource(_blendMode.ShaderName));
            }

            _shader.Parameters.Set(TerrainLayerKeys.InputTexture, input);
            _shader.Parameters.Set(TerrainLayerKeys.OutputTexture, groupOutput ?? _cachedOutput);
            _shader.Parameters.Set(TerrainLayerKeys.MaxHeight, terrainSettings.MaxHeight);
            _shader.Parameters.Set(TerrainLayerKeys.UnitsPerTexel, terrainSettings.UnitsPerTexel);
            _shader.Parameters.Set(TerrainLayerKeys.InvUnitsPerTexel, 1.0f / terrainSettings.UnitsPerTexel);
            _shader.Parameters.Set(TerrainLayerKeys.Resolution, (uint)terrainSettings.Resolution);
            _shader.Parameters.Set(TerrainLayerKeys.InvResolution, 1.0f / terrainSettings.Resolution);
            _shader.Parameters.Set(TerrainLayerKeys.InvSize, 1.0f / terrainSettings.Size);
            _shader.Parameters.Set(TerrainLayerKeys.Size, terrainSettings.Size);

            _type.SetShaderParameters(renderDrawContext, _shader.Parameters);
            _blendMode.SetShaderParameters(_shader.Parameters);

            _shader.ThreadGroupCounts = new(ComputeHelpers.DispatchSize(8, terrainSettings.Resolution), ComputeHelpers.DispatchSize(8, terrainSettings.Resolution), 1);
            _shader.ThreadNumbers = new(8, 8, 1);

            if (_type is ITerrainLayerCustomDraw customDraw)
            {
                customDraw.Draw(terrainSettings, renderDrawContext, _shader, input, _cachedOutput!);
            }
            else
            {
                _shader.Draw(renderDrawContext, _type.GetType().Name);
            }

            if (groupOutput != null)
            {
                renderDrawContext.CommandList.Copy(groupOutput, _cachedOutput);
            }

            return (_cachedOutput!, true);
        }
        else
        {
            return (_cachedOutput, false);
        }
    }

    private bool IsCachedTextureValid(TerrainSettings terrainSettings)
        => _cachedOutput != null && _cachedOutput.Width == terrainSettings.Resolution;
}

[JsonDerivedType(typeof(CircleLayer), 1)]
[JsonDerivedType(typeof(ConstantValuetLayer), 2)]
[JsonDerivedType(typeof(ErosionLayer), 3)]
[JsonDerivedType(typeof(FbmLayer), 4)]
[JsonDerivedType(typeof(GroupLayer), 5)]
[JsonDerivedType(typeof(RidgedLayer), 6)]
[JsonDerivedType(typeof(TurbulenceLayer), 7)]
public interface ITerrainLayerType
{
    string ShaderName { get; }
    bool IsDirty { get; }
    void DrawUi();
    void SetShaderParameters(RenderDrawContext renderDrawContext, ParameterCollection parameterCollection);
}

public interface ITerrainLayerCustomDraw
{
    void Draw(TerrainSettings terrainSettings, RenderDrawContext renderDrawContext, ComputeEffectShader shader, Texture input, Texture output);
}

[JsonDerivedType(typeof(BlendAdd), 1)]
[JsonDerivedType(typeof(BlendAlphaBlend), 2)]
[JsonDerivedType(typeof(BlendMax), 3)]
[JsonDerivedType(typeof(BlendMultiply), 4)]
[JsonDerivedType(typeof(BlendNone), 5)]
[JsonDerivedType(typeof(BlendSubtract), 6)]
public interface IBlendMode
{
    string ShaderName { get; }
    bool IsDirty { get; }
    void DrawUi();
    void SetShaderParameters(ParameterCollection parameterCollection);
}

public static class ComputeTerrainLayerEffectKeys
{
    public static readonly PermutationParameterKey<ShaderSource> Layer = ParameterKeys.NewPermutation<ShaderSource>();
    public static readonly PermutationParameterKey<ShaderSource> BlendMode = ParameterKeys.NewPermutation<ShaderSource>();
}
