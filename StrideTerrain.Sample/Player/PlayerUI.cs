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

public class PlayerUI : StartupScript
{
    public required Texture MiniMap { get; set; }
    public required TransformComponent PlayerTransform { get; set; }
    public required TransformComponent PlayerRotationTransform { get; set; }

    private PlayerInterface _playerInterface;

    [DataMemberIgnore]
    public bool ShowFullMap
    {
        get => _playerInterface.ShowFullMap;
        set => _playerInterface.ShowFullMap = value;
    }

    public override void Start()
    {
        base.Start();

        _playerInterface = new PlayerInterface(Services)
        {
            PlayerTransform = PlayerTransform,
            PlayerRotationTransform = PlayerRotationTransform,
            MiniMap = MiniMap,
        };
    }

    public override void Cancel()
    {
        base.Cancel();
        _playerInterface.Dispose();
        _playerInterface = null;
    }

    private class PlayerInterface(IServiceRegistry services) : BaseWindow(services)
    {
        public TransformComponent PlayerTransform;
        public TransformComponent PlayerRotationTransform;
        public Texture MiniMap;

        private const int CompassSize = 300;
        private const int CompassMargin = 32;
        private const int MapMargin = 32;

        protected override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

        private Vector2 _windowSize;
        protected override System.Numerics.Vector2? WindowSize => _windowSize;

        private Vector2 _windowPosition;
        protected override System.Numerics.Vector2? WindowPos => _windowPosition;

        public bool ShowFullMap { get; set; } = false;

        protected override void OnDestroy()
        {
        }

        public override void Update(GameTime gameTime)
        {
            var windowSize = new Vector2(Game.Window.ClientBounds.Width, Game.Window.ClientBounds.Height);
            var windowCenter = windowSize / 2;

            if (ShowFullMap)
            {
                var size = new Vector2(MiniMap.Width + MapMargin * 2, MiniMap.Height + MapMargin * 2);

                _windowPosition = windowCenter - size / 2.0f;
                _windowSize = size;
            }
            else
            {
                _windowSize = new Vector2(CompassSize + CompassMargin, CompassSize + CompassMargin);
                _windowPosition= new (windowSize.X - _windowSize.X - CompassMargin, CompassMargin);
            }

            base.Update(gameTime);
        }

        protected override void OnDraw(bool collapsed)
        {
            var playerPosition = PlayerTransform.Position.XZ();
            var playerPositionTerrain = playerPosition * 2.0f; // TODO: replace with terrain data
            float terrainSize = 8192.0f;
            float minimapTextureSize = 1024.0f;
            float terrainToMinimap = minimapTextureSize / terrainSize;

            var drawList = GetWindowDrawList();
            var position = (Vector2)GetCursorScreenPos();

            if (ShowFullMap)
            {
                position += new Vector2(MapMargin, MapMargin);
                var size = new Vector2(MiniMap.Width, MiniMap.Height);
                var border = new Vector2(8, 8);

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
                DrawPlayerArrow(drawList, position, playerMiniMapPos);
            }
            else
            {
                float zoom = 1.5f;

                float halfSizeInWorld = (CompassSize / terrainToMinimap) / (2 * zoom);

                var topLeftWorld = playerPositionTerrain - new Vector2(halfSizeInWorld, halfSizeInWorld);

                Vector2 WorldToMinimap(Vector2 worldPos)
                {
                    var relativePos = worldPos - topLeftWorld;
                    var normalized = relativePos / (halfSizeInWorld * 2); // 0..1
                    return normalized * CompassSize; // to minimap pixels
                }

                var center = position + new Vector2(CompassSize / 2, CompassSize / 2);
                float radius = CompassSize / 2;

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
                var playerMiniMapPos = WorldToMinimap(playerPositionTerrain);
                DrawPlayerArrow(drawList, position, playerMiniMapPos);
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

            void DrawPlayerArrow(ImDrawListPtr drawList, Vector2 position, Vector2 playerMiniMapPos)
            {
                float arrowLength = 8.0f;
                float arrowWidth = 4.0f;

                // Arrow points relative to origin (pointing up)
                Vector2 tip = new(0, -arrowLength);
                Vector2 leftBase = new(-arrowWidth, arrowLength / 2);
                Vector2 rightBase = new(arrowWidth, arrowLength / 2);

                float playerYaw = -PlayerRotationTransform.Rotation.YawPitchRoll.X;

                // Rotate points around player and offset by minimap position
                Vector2 rpTip = position + playerMiniMapPos + Rotate(tip, playerYaw);
                Vector2 rpLeft = position + playerMiniMapPos + Rotate(leftBase, playerYaw);
                Vector2 rpRight = position + playerMiniMapPos + Rotate(rightBase, playerYaw);

                drawList.AddTriangleFilled(
                    rpTip,
                    rpLeft,
                    rpRight,
                    GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1f))
                );
            }
        }
    }
}
