using Hexa.NET.ImGui;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using StrideCommunity.ImGuiDebug;
using System;
using static Hexa.NET.ImGui.ImGui;
using static StrideCommunity.ImGuiDebug.ImGuiExtension;

namespace StrideTerrain.Sample.Player;

class PlayerInterface(IServiceRegistry services) : BaseWindow(services)
{
    public TransformComponent? PlayerTransform;
    public TransformComponent? PlayerRotationTransform;
    public TransformComponent? PlayerCameraRotationTransform;
    public Texture? MiniMap;

    private const int CompassSize = 300;
    private const int CompassMargin = 32;
    private const int MapMargin = 32;

    protected override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    private Vector2 _windowSize;
    protected override System.Numerics.Vector2? WindowSize => _windowSize;

    private Vector2 _windowPosition;
    protected override System.Numerics.Vector2? WindowPos => _windowPosition;

    public bool ShowFullMap { get; set; } = false;
    public bool ShowCompass { get; set; } = true;

    private bool IsValid => MiniMap != null && PlayerTransform != null && PlayerRotationTransform != null && PlayerCameraRotationTransform != null;

    protected override void OnDestroy()
    {
    }

    public override void Update(GameTime gameTime)
    {
        if (!IsValid)
            return;

        var windowSize = new Vector2(Game.Window.ClientBounds.Width, Game.Window.ClientBounds.Height);
        var windowCenter = windowSize / 2;

        if (ShowFullMap)
        {
            var size = new Vector2(MiniMap!.Width + MapMargin * 2, MiniMap.Height + MapMargin * 2);

            _windowPosition = windowCenter - size / 2.0f;
            _windowSize = size;
        }
        else
        {
            _windowSize = new Vector2(CompassSize + CompassMargin * 2, CompassSize + CompassMargin * 2);
            _windowPosition = new(windowSize.X - _windowSize.X - CompassMargin, CompassMargin);
        }

        base.Update(gameTime);
    }

    protected override void OnDraw(bool collapsed)
    {
        if (!IsValid)
            return;

        var playerPosition = PlayerTransform!.Position.XZ();
        var playerPositionTerrain = playerPosition * 2.0f; // TODO: replace with terrain data
        float terrainSize = 8192.0f;
        float minimapTextureSize = 1024.0f;
        float terrainToMinimap = minimapTextureSize / terrainSize;

        var drawList = GetWindowDrawList();
        var position = (Vector2)GetCursorScreenPos();

        if (ShowFullMap)
        {
            DrawMap(playerPositionTerrain, terrainToMinimap, drawList, position);
        }
        else
        {
            DrawCompass(playerPositionTerrain, minimapTextureSize, terrainToMinimap, drawList, position);
        }

        void DrawPlayerArrow(ImDrawListPtr drawList, Vector2 position, uint color, float arrowLength = 12, float arrowWidth = 6)
        {
            // Arrow points relative to origin (pointing up)
            Vector2 tip = new(0, -arrowLength);
            Vector2 leftBase = new(-arrowWidth, arrowLength / 2);
            Vector2 rightBase = new(arrowWidth, arrowLength / 2);

            float playerYaw = (-PlayerRotationTransform!.Rotation.YawPitchRoll.X);

            // Rotate points around player and offset by minimap position
            Vector2 rpTip = position + Rotate(tip, playerYaw);
            Vector2 rpLeft = position + Rotate(leftBase, playerYaw);
            Vector2 rpRight = position + Rotate(rightBase, playerYaw);

            drawList.AddTriangleFilled(
                rpTip,
                rpLeft,
                rpRight,
                color
            );
        }

        void DrawCompass(Vector2 playerPositionTerrain, float minimapTextureSize, float terrainToMinimap, ImDrawListPtr drawList, Vector2 position)
        {
            position += new Vector2(CompassMargin, CompassMargin);

            const float zoom = 1.5f;
            float halfSizeInWorld = (CompassSize / terrainToMinimap) / (2 * zoom);
            var topLeftWorld = playerPositionTerrain - new Vector2(halfSizeInWorld, halfSizeInWorld);

            // TODO: Rotate everyting around this point
            float playerCameraYaw = -PlayerCameraRotationTransform!.Rotation.YawPitchRoll.X;

            var center = position + new Vector2(CompassSize / 2, CompassSize / 2);
            float radius = CompassSize / 2;

            // Draw north arrow
            const float northTriangleSize = 8;
            const float northTriangleBorderSize = 11;
            drawList.AddTriangleFilled(
                new(center.X + -northTriangleBorderSize, center.Y - radius),
                new(center.X + northTriangleBorderSize, center.Y - radius),
                new(center.X, center.Y - radius - northTriangleBorderSize * 2),
                GetColorU32(new Vector4(0, 0, 0, 1f))
            );

            drawList.AddTriangleFilled(
                new(center.X + -northTriangleSize, center.Y - radius),
                new(center.X + northTriangleSize, center.Y - radius),
                new(center.X, center.Y - radius - northTriangleSize * 2),
                GetColorU32(new Vector4(1, 1, 1, 1f))
            );

            // Draw minimap texture
            drawList.AddImageRounded(
                GetTextureKey(MiniMap),
                position,
                position + new Vector2(CompassSize, CompassSize),
                new Vector2(topLeftWorld.X * terrainToMinimap / minimapTextureSize,
                            topLeftWorld.Y * terrainToMinimap / minimapTextureSize),
                new Vector2((topLeftWorld.X + halfSizeInWorld * 2) * terrainToMinimap / minimapTextureSize,
                            (topLeftWorld.Y + halfSizeInWorld * 2) * terrainToMinimap / minimapTextureSize),
                GetColorU32(new Vector4(1, 1, 1, 1)),
                radius
            );

            // Border
            drawList.AddCircle(center, radius, GetColorU32(new Vector4(0, 0, 0, 1)), 64, 3.0f);
            drawList.AddCircle(center, radius - 1.5f, GetColorU32(new Vector4(1, 1, 1, 1)), 64, 1.0f);

            // Draw player arrow
            var playerMiniMapPos = WorldToCompass(playerPositionTerrain);
            DrawPlayerArrow(drawList, position + playerMiniMapPos, GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1)), 12, 6);
            DrawPlayerArrow(drawList, position + playerMiniMapPos, GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1)), 8, 4);

            Vector2 WorldToCompass(Vector2 worldPos)
            {
                var relativePos = worldPos - topLeftWorld;
                var normalized = relativePos / (halfSizeInWorld * 2);
                return normalized * CompassSize;
            }
        }

        void DrawMap(Vector2 playerPositionTerrain, float terrainToMinimap, ImDrawListPtr drawList, Vector2 position)
        {
            position += new Vector2(MapMargin, MapMargin);
            var size = new Vector2(MiniMap!.Width, MiniMap.Height);

            drawList.AddRect(position, position + size, GetColorU32(new Vector4(0, 0, 0, 1)), 0f, 8f);
            drawList.AddRect(position, position + size, GetColorU32(new Vector4(1, 1, 1, 1)), 0f, 3f);

            drawList.AddImage(
                GetTextureKey(MiniMap),
                position,
                position + size,
                new Vector2(0, 0),
                new Vector2(1, 1),
                GetColorU32(new Vector4(1, 1, 1, 1.0f))
            );

            var playerMiniMapPos = playerPositionTerrain * terrainToMinimap;
            DrawPlayerArrow(drawList, position + playerMiniMapPos, GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1)), 12, 6);
            DrawPlayerArrow(drawList, position + playerMiniMapPos, GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1)), 8, 4);
        }

        static Vector2 Rotate(Vector2 v, float angle)
        {
            float cosA = MathF.Cos(angle);
            float sinA = MathF.Sin(angle);
            return new Vector2(
                v.X * cosA - v.Y * sinA,
                v.X * sinA + v.Y * cosA
            );
        }
    }
}
