using Hexa.NET.ImGui;
using IconFonts;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Colors;
using Stride.Rendering.ComputeEffect;
using Stride.Rendering.Lights;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using StrideCommunity.ImGuiDebug;
using StrideTerrain.Editor.BlendModes;
using StrideTerrain.Editor.Effects;
using StrideTerrain.Editor.Layers;
using StrideTerrain.Editor.Rendering;
using StrideTerrain.Rendering;
using StrideTerrain.Weather;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Hexa.NET.ImGui.ImGui;
using static StrideCommunity.ImGuiDebug.ImGuiExtension;

namespace StrideTerrain.Editor;

public class EditorGame : Game
{
    public const PixelFormat TerrainPixelFormat = PixelFormat.R16_UNorm;

    private Entity _cameraEntity = null!;
    private LightComponent _light = null!;
    private WeatherComponent _weather = null!;
    private ModelComponent _terrainModelComponent = null!;
    private Material _terrainMaterial = null!;

    private EditorContext _context = new();
    private Texture? _normalMapTexture;
    private TerrainLayer _terrainClear = new()
    {
        Type = new ConstantValuetLayer()
        {
        }
    };

    private ComputeEffectShader _normalMapShader = null!;
    private TerrainMeshManager _terrainMeshManager = null!;

    protected override void Initialize()
    {
        base.Initialize();

        // Set the window in a sane position
        // TODO: This should center the window instead
        Window.Position = new Int2(10, 10);
        //Window.FullscreenIsBorderlessWindow = true;
    }

    protected override void BeginRun()
    {
        base.BeginRun();

        // Fix Update order
        ((GameSystemBase)GameSystems.First(x => x is InputSystem)).UpdateOrder = -2;

        var imGuiSystem = new ImGuiSystem(Services, GraphicsDeviceManager, addFonts: AddFonts)
        {
            UpdateOrder = -1
        };

        SetupImguiStyle();

        //new HierarchyView(Services);

        // Setup UI
        new TerrainEditorWindow(Services, _context);

        var scene = SceneSystem.SceneInstance.RootScene;

        // Setup camera
        _cameraEntity = scene.Entities.First();
        _cameraEntity.Add(new BasicCameraController());

        _cameraEntity.Transform.Position = new(0, 100, 0);
        _cameraEntity.Transform.Rotation = Quaternion.RotationY(MathUtil.DegreesToRadians(220));

        // Setup terrain entity
        _terrainMeshManager = new(GraphicsDevice);
        var meshRenderFeature = (MeshRenderFeature)SceneSystem.GraphicsCompositor.RenderFeatures.First(x => x is MeshRenderFeature);
        meshRenderFeature.RenderFeatures.Add(new EditorTerrainRenderFeature
        {
            MeshManager = _terrainMeshManager
        });

        _terrainMaterial = Material.New(GraphicsDevice, new MaterialDescriptor
        {
            Attributes = new MaterialAttributes
            {
                Displacement = new EditorTerrainDisplacementFeature(),
                MicroSurface = new MaterialGlossinessMapFeature
                {
                    GlossinessMap = new ComputeShaderClassScalar
                    {
                        MixinReference = "TerrainComputeColorRoughness"
                    },
                    Invert = true
                },
                Diffuse = new EditorTerrainDiffuseFeature()
                {
                    DiffuseTextureArray = Content.Load<Texture>("Valley_diffuse"),
                    NormalTextureArray = Content.Load<Texture>("Valley_normal"),
                    RoughnessTextureArray = Content.Load<Texture>("Valley_roughness"),
                },
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                Specular = new MaterialMetalnessMapFeature
                {
                    MetalnessMap = new ComputeFloat(0.0f)
                },
                SpecularModel = new MaterialSpecularMicrofacetModelFeature
                {
                    Environment = new MaterialSpecularMicrofacetEnvironmentGGXPolynomial()
                }
            }
        });

        _terrainModelComponent = new ModelComponent()
        {
            Model = [_terrainMeshManager.Mesh],
            IsShadowCaster = true
        };

        _terrainModelComponent.Materials[0] = _terrainMaterial;

        scene.Entities.Add([_terrainModelComponent]);

        // Setup atmosphere and lighting
        var lightDirectional = new LightDirectional
        {
            Color = new ColorRgbProvider(Color.White),
        };
        lightDirectional.Shadow.Enabled = true;
        lightDirectional.Shadow.Size = LightShadowMapSize.XLarge;
        lightDirectional.Shadow.CascadeCount = LightShadowMapCascadeCount.FourCascades;
        lightDirectional.Shadow.Filter = new LightShadowMapFilterTypePcf
        {
            FilterSize = LightShadowMapFilterTypePcfSize.Filter7x7
        };
        //lightDirectional.Shadow.DepthRange.IsAutomatic = false;
        //lightDirectional.Shadow.DepthRange.ManualMaxDistance = 512;

        _light = new()
        {
            Type = lightDirectional,
            Intensity = 5
        };

        _weather = new()
        {
            Sun = _light,
            Fog = new()
            {
                Density = 0
            },
            Clouds = new()
            {
            }
        };

        Entity atmosphere = [_weather, _light];
        atmosphere.Transform.Rotation = Quaternion.RotationX(MathUtil.DegreesToRadians(-160)) * Quaternion.RotationY(MathUtil.DegreesToRadians(-90));
        scene.Entities.Add(atmosphere);
    }

    private void SetupImguiStyle()
    {
        var style = GetStyle();

        // Borders
        style.WindowBorderSize = 3.0f;

        // Rounding
        style.FrameRounding = 3.0f;
        style.PopupRounding = 3.0f;
        style.ScrollbarRounding = 3.0f;
        style.GrabRounding = 3.0f;
        style.WindowRounding = 3.0f;
        style.ChildRounding = 3.0f;
        style.TabRounding = 3.0f;

        // Colors!
        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = ToRGBA(0xFFABB2BF);
        colors[(int)ImGuiCol.TextDisabled] = ToRGBA(0xFF565656);
        colors[(int)ImGuiCol.WindowBg] = ToRGBA(0xFF282C34);
        colors[(int)ImGuiCol.ChildBg] = ToRGBA(0xFF21252B);
        colors[(int)ImGuiCol.PopupBg] = ToRGBA(0xFF2E323A);
        colors[(int)ImGuiCol.Border] = ToRGBA(0xFF2E323A);
        colors[(int)ImGuiCol.BorderShadow] = ToRGBA(0x00000000);
        colors[(int)ImGuiCol.FrameBg] = colors[(int)ImGuiCol.ChildBg];
        colors[(int)ImGuiCol.FrameBgHovered] = ToRGBA(0xFF484C52);
        colors[(int)ImGuiCol.FrameBgActive] = ToRGBA(0xFF54575D);
        colors[(int)ImGuiCol.TitleBg] = colors[(int)ImGuiCol.WindowBg];
        colors[(int)ImGuiCol.TitleBgActive] = colors[(int)ImGuiCol.FrameBgActive];
        colors[(int)ImGuiCol.TitleBgCollapsed] = ToRGBA(0x8221252B);
        colors[(int)ImGuiCol.MenuBarBg] = colors[(int)ImGuiCol.ChildBg];
        colors[(int)ImGuiCol.ScrollbarBg] = colors[(int)ImGuiCol.PopupBg];
        colors[(int)ImGuiCol.ScrollbarGrab] = ToRGBA(0xFF3E4249);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = ToRGBA(0xFF484C52);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = ToRGBA(0xFF54575D);
        colors[(int)ImGuiCol.CheckMark] = colors[(int)ImGuiCol.Text];
        colors[(int)ImGuiCol.SliderGrab] = ToRGBA(0xFF353941);
        colors[(int)ImGuiCol.SliderGrabActive] = ToRGBA(0xFF7A7A7A);
        colors[(int)ImGuiCol.Button] = colors[(int)ImGuiCol.SliderGrab];
        colors[(int)ImGuiCol.ButtonHovered] = colors[(int)ImGuiCol.FrameBgActive];
        colors[(int)ImGuiCol.ButtonActive] = colors[(int)ImGuiCol.ScrollbarGrabActive];
        colors[(int)ImGuiCol.Header] = colors[(int)ImGuiCol.ChildBg];
        colors[(int)ImGuiCol.HeaderHovered] = ToRGBA(0xFF353941);
        colors[(int)ImGuiCol.HeaderActive] = colors[(int)ImGuiCol.FrameBgActive];
        colors[(int)ImGuiCol.Separator] = colors[(int)ImGuiCol.FrameBgActive];
        colors[(int)ImGuiCol.SeparatorHovered] = ToRGBA(0xFF3E4452);
        colors[(int)ImGuiCol.SeparatorActive] = colors[(int)ImGuiCol.SeparatorHovered];
        colors[(int)ImGuiCol.ResizeGrip] = colors[(int)ImGuiCol.Separator];
        colors[(int)ImGuiCol.ResizeGripHovered] = colors[(int)ImGuiCol.SeparatorHovered];
        colors[(int)ImGuiCol.ResizeGripActive] = colors[(int)ImGuiCol.SeparatorActive];
        colors[(int)ImGuiCol.TabHovered] = colors[(int)ImGuiCol.HeaderHovered];
        colors[(int)ImGuiCol.Tab] = colors[(int)ImGuiCol.FrameBgActive];
        colors[(int)ImGuiCol.TabSelected] = colors[(int)ImGuiCol.HeaderHovered];
        colors[(int)ImGuiCol.TabSelectedOverline] = colors[(int)ImGuiCol.HeaderActive];
        colors[(int)ImGuiCol.TabDimmed] = Lerp(colors[(int)ImGuiCol.Tab], colors[(int)ImGuiCol.TitleBg], 0.80f);
        colors[(int)ImGuiCol.TabDimmedSelected] = Lerp(colors[(int)ImGuiCol.TabSelected], colors[(int)ImGuiCol.TitleBg], 0.40f);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = new(0.50f, 0.50f, 0.50f, 0.00f);
        colors[(int)ImGuiCol.DockingPreview] = colors[(int)ImGuiCol.ChildBg];
        colors[(int)ImGuiCol.DockingEmptyBg] = colors[(int)ImGuiCol.WindowBg];
        colors[(int)ImGuiCol.PlotLines] = new(0.61f, 0.61f, 0.61f, 1.00f);
        colors[(int)ImGuiCol.PlotLinesHovered] = new(1.00f, 0.43f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogram] = new(0.90f, 0.70f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogramHovered] = new(1.00f, 0.60f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = colors[(int)ImGuiCol.ChildBg];
        colors[(int)ImGuiCol.TableBorderStrong] = colors[(int)ImGuiCol.SliderGrab];
        colors[(int)ImGuiCol.TableBorderLight] = colors[(int)ImGuiCol.FrameBgActive];
        colors[(int)ImGuiCol.TableRowBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.TableRowBgAlt] = new(1.00f, 1.00f, 1.00f, 0.06f);
        colors[(int)ImGuiCol.TextLink] = ToRGBA(0xFF3F94CE);
        colors[(int)ImGuiCol.TextSelectedBg] = ToRGBA(0xFF243140);
        colors[(int)ImGuiCol.DragDropTarget] = colors[(int)ImGuiCol.Text];
        colors[(int)ImGuiCol.NavWindowingHighlight] = colors[(int)ImGuiCol.Text];
        colors[(int)ImGuiCol.NavWindowingDimBg] = new(0.80f, 0.80f, 0.80f, 0.20f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = ToRGBA(0xC821252B);

        static Vector4 ToRGBA(uint argb)
        {
            var color = new Vector4
            {
                X = (argb >> 16 & 0xFF) / 255.0f,
                Y = (argb >> 8 & 0xFF) / 255.0f,
                Z = (argb & 0xFF) / 255.0f,
                W = (argb >> 24 & 0xFF) / 255.0f
            };
            return color;
        }

        static Vector4 Lerp(Vector4 a, Vector4 b, float t)
            => Vector4.Lerp(a, b, t);
    }

    static unsafe void AddFonts()
    {
        ImGuiIOPtr io = GetIO();

        var config = new ImFontConfig
        {
            FontDataOwnedByAtlas = 1,
            OversampleH = 1,
            OversampleV = 1,
            GlyphMaxAdvanceX = float.MaxValue,
            RasterizerMultiply = 1,
            RasterizerDensity = 1
        };

        AddFont("StrideTerrain.Editor.Resources.Roboto-Regular.ttf", 18, config, io.Fonts.GetGlyphRangesDefault());

        config.MergeMode = 1;

        var ranges = new ushort[] { ForkAwesome.IconMin, ForkAwesome.IconMax16, 0 };
        fixed (ushort* pRanges = ranges)
        {
            AddFont("StrideTerrain.Editor.Resources.forkawesome-webfont.ttf", 16, config, (char*)pRanges);
        }

        io.Fonts.Build();

        static unsafe void AddFont(string name, float size, ImFontConfig config, char* glypRanges)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            using var reader = new BinaryReader(stream!);

            var data = reader.ReadBytes((int)stream!.Length);

            ImGuiIOPtr io = GetIO();
            fixed (void* pData = data)
            {
                io.Fonts.AddFontFromMemoryTTF(pData, data.Length, size, &config, glypRanges);
            }
        }
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _terrainMeshManager.Mesh.BoundingBox = new BoundingBox(Vector3.Zero, Vector3.One * _context.Terrain.Size);
        _terrainMeshManager.Mesh.BoundingSphere = new(Vector3.One * _context.Terrain.Size * 0.5f, _context.Terrain.Size * 0.5f);

        _terrainMeshManager.Update(_context.Terrain, _cameraEntity.Transform.Position);

        (_light.Type as LightDirectional).Shadow.Enabled = _context.Terrain.EnableShadows;
        _light.Entity.Transform.Rotation = Quaternion.RotationX(MathUtil.DegreesToRadians(_context.Terrain.SunAngles.X)) * Quaternion.RotationY(MathUtil.DegreesToRadians(-_context.Terrain.SunAngles.Y));
    }

    private (Texture TerrainTexture, bool WasDirty) EvaluateLayerStack(RenderDrawContext renderDrawContext, List<TerrainLayer> layers)
    {
        var (terrainTexture, _) = _terrainClear.Render(_context.Terrain, renderDrawContext, null!, false);
        var anyDirty = false;
        foreach (var layer in layers)
        {
            if (layer.IsGroup)
            {
                (Texture groupOutput, bool groupDirty) = EvaluateLayerStack(renderDrawContext, layer.Children);
                anyDirty |= groupDirty;

                (terrainTexture, bool wasDirty) = layer.Render(_context.Terrain, renderDrawContext, terrainTexture, anyDirty, groupOutput);
                anyDirty |= wasDirty;
            }
            else
            {
                (terrainTexture, bool wasDirty) = layer.Render(_context.Terrain, renderDrawContext, terrainTexture, anyDirty);
                anyDirty |= wasDirty;
            }
        }

        return (terrainTexture, anyDirty);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_normalMapTexture != null)
        {
            GraphicsContext.Allocator.ReleaseReference(_normalMapTexture);
        }

        var renderContext = RenderContext.GetShared(Services);
        var renderDrawContext = renderContext.GetThreadContext();

        // Process layer stack
        var (terrainTexture, _) = EvaluateLayerStack(renderDrawContext, _context.Terrain.Layers);

        // Calculate normal map
        _normalMapShader ??= new(renderContext)
        {
            ShaderSourceName = "CalculateNormalMap"
        };

        _normalMapTexture = GraphicsContext.Allocator.GetTemporaryTexture2D(_context.Terrain.Resolution, _context.Terrain.Resolution, PixelFormat.R16G16B16A16_UNorm, TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);

        _normalMapShader.Parameters.Set(CalculateNormalMapKeys.InputTexture, terrainTexture);
        _normalMapShader.Parameters.Set(CalculateNormalMapKeys.OutputTexture, _normalMapTexture);
        _normalMapShader.Parameters.Set(CalculateNormalMapKeys.MaxHeight, _context.Terrain.MaxHeight);
        _normalMapShader.ThreadGroupCounts = new(ComputeHelpers.DispatchSize(8, _context.Terrain.Resolution), ComputeHelpers.DispatchSize(8, _context.Terrain.Resolution), 1);
        _normalMapShader.ThreadNumbers = new(8, 8, 1);
        _normalMapShader.Draw(renderDrawContext, "Terrain Normal Map");

        // Prepare terrain rendering
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainDisplacementKeys.Heightmap, terrainTexture);
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainMaterialStreamInitializerKeys.TerrainNormalMap, _normalMapTexture);
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainMaterialStreamInitializerKeys.TerrainTextureSize, _context.Terrain.Resolution);
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainMaterialStreamInitializerKeys.MaxHeight, _context.Terrain.MaxHeight);

        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainDisplacementKeys.UnitsPerTexel, _context.Terrain.UnitsPerTexel);
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainDisplacementKeys.InvUnitsPerTexel, 1.0f / _context.Terrain.UnitsPerTexel);
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainDisplacementKeys.MaxHeight, _context.Terrain.MaxHeight);
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainDisplacementKeys.InvTerrainTextureSize, 1.0f / _context.Terrain.Resolution);
        _terrainMaterial.Passes[0].Parameters.Set(EditorTerrainDisplacementKeys.Resolution, (uint)_context.Terrain.Resolution);

        base.Draw(gameTime);
    }

    public override void ConfirmRenderingSettings(bool gameCreation)
    {
        base.ConfirmRenderingSettings(gameCreation);

        var deviceManager = (GraphicsDeviceManager)graphicsDeviceManager;
        deviceManager.PreferredDepthStencilFormat = PixelFormat.D32_Float_S8X24_UInt;

        //Profiler.EnableAll();
        //GraphicsDeviceManager.DeviceCreationFlags |= DeviceCreationFlags.Debug;
    }
}


public record TypeInfo(Type Type, DisplayAttribute? Display);

public class TerrainEditorWindow(IServiceRegistry services, EditorContext context) : BaseWindow(services)
{
    private TerrainLayer? _selectedLayer;

    protected override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoTitleBar;

    private FilePicker _openFilePicker = new FilePicker("###OPEN", true);
    private FilePicker _saveFilePicker = new FilePicker("###SAVE", false);

    private enum LayerOp
    {
        None,
        Delete,
        MoveUp,
        MoveDown
    }

    protected override void OnDestroy()
    {
    }

    protected override void OnDraw(bool collapsed)
    {
        var terrainSettings = context.Terrain;
        var layers = context.Terrain.Layers;

        if (_openFilePicker.Draw())
        {
            context.Load(File.ReadAllText(_openFilePicker.SelectedFile!));
            _selectedLayer = null;
        }
        if (_saveFilePicker.Draw())
        {
            var json = context.Save();
            File.WriteAllText(_saveFilePicker.SelectedFile!, json);
        }

        if (Button(ForkAwesome.FolderOpen))
        {
            _openFilePicker.Show();
        }
        SameLine();
        if (Button(ForkAwesome.FloppyO))
        {
            _saveFilePicker.Show();
        }
        SameLine();
        using (ID("SunAngles"))
        {
            SliderFloat2("", ref terrainSettings.SunAngles, -360, 360);
        }
        SameLine();
        if (Button(terrainSettings.EnableShadows ? ForkAwesome.Sun : ForkAwesome.SunO))
        {
            terrainSettings.EnableShadows = !terrainSettings.EnableShadows;
        }

        SeparatorText("Terrain Configuration");

        terrainSettings.IsDirty = false;
        terrainSettings.IsDirty |= Combo("Resolution", ref terrainSettings.ResolutionIndex, TerrainSettings.ResolutionStrings, TerrainSettings.ResolutionStrings.Length);

        terrainSettings.IsDirty |= DragFloat("Size (m)", ref terrainSettings.Size);
        terrainSettings.Size = Math.Max(terrainSettings.Size, 1);

        Text($"Meters Per Texel: {terrainSettings.UnitsPerTexel:0.00}");

        terrainSettings.IsDirty |= DragFloat("Max Height", ref terrainSettings.MaxHeight);
        terrainSettings.MaxHeight = Math.Max(terrainSettings.MaxHeight, 0.1f);

        SeparatorText("Layers");

        RenderLayerList(layers);

        var maxWidth = GetContentRegionAvail().X;
        var spacingX = GetStyle().ItemSpacing.X;
        if (Button(ForkAwesome.Plus, new(maxWidth - spacingX, 24)))
        {
            layers.Add(new());
            _selectedLayer = layers[^1];
        }

        if (_selectedLayer != null)
        {
            SeparatorText("Layer Settings");
            _selectedLayer.DrawUi();
        }

        int RenderLayerList(List<TerrainLayer> layers, int id = 0)
        {
            TerrainLayer? layerToOperateOn = null;
            var layerOp = LayerOp.None;
            for (var i = 0; i < layers.Count; i++)
            {
                id++;
                using var _ = ID(id);

                var layer = layers[i];
                var name = string.IsNullOrEmpty(layer.Name) ? layer.Type == null ? "(No Type)" : TypeInfoCache.GetLayerName(layer.Type) : layer.Name;

                if (layer.IsGroup)
                {
                    name = $"{ForkAwesome.Folder} {name}";
                }

                if (layer.BlendMode.GetType() != typeof(BlendNone))
                {
                    name = $"{name} ({TypeInfoCache.GetBlendModeName(layer.BlendMode)})";
                }

                if (Button(name))
                {
                    _selectedLayer = layer;
                }
                if (layer.IsGroup)
                {
                    SameLine(GetWindowWidth() - 160);
                    if (Button(ForkAwesome.Plus))
                    {
                        layer.Children.Add(new());
                        _selectedLayer = layer.Children[^1];
                    }
                }
                SameLine(GetWindowWidth() - 130);
                Checkbox("", ref layer.Enabled);
                SameLine(GetWindowWidth() - 100);
                if (Button(ForkAwesome.Trash))
                {
                    layerToOperateOn = layer;
                    layerOp = LayerOp.Delete;
                }
                SameLine(GetWindowWidth() - 70);
                if (Button(ForkAwesome.ArrowUp))
                {
                    layerToOperateOn = layer;
                    layerOp = LayerOp.MoveUp;
                }
                SameLine(GetWindowWidth() - 40);
                if (Button(ForkAwesome.ArrowDown))
                {
                    layerToOperateOn = layer;
                    layerOp = LayerOp.MoveDown;
                }

                if (layer.IsGroup)
                {
                    using (UIndent())
                    {
                        id = RenderLayerList(layer.Children, id);
                    }
                }
            }

            if (layerToOperateOn != null)
            {
                switch (layerOp)
                {
                    case LayerOp.None:
                        break;
                    case LayerOp.Delete:
                        layers.Remove(layerToOperateOn);
                        _selectedLayer = null;
                        break;
                    case LayerOp.MoveDown:
                        {
                            var i = layers.IndexOf(layerToOperateOn);
                            if (i < layers.Count - 1)
                            {
                                layers[i] = layers[i + 1];
                                layers[i + 1] = layerToOperateOn;
                            }
                        }
                        break;
                    case LayerOp.MoveUp:
                        {
                            var i = layers.IndexOf(layerToOperateOn);
                            if (i > 0)
                            {
                                layers[i] = layers[i - 1];
                                layers[i - 1] = layerToOperateOn;
                            }
                        }
                        break;
                }
            }

            return id;
        }
    }
}
