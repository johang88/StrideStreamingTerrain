using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;
using StrideTerrain.Common;
using StrideTerrain.TerrainSystem.Physics;
using StrideTerrain.TerrainSystem.Rendering;
using StrideTerrain.TerrainSystem.Streaming;
using System;

namespace StrideTerrain.TerrainSystem;

[DataContract]
public sealed class TerrainRuntimeData : IDisposable
{
    public const int RuntimeTextureSize = 4096;
    public const float InvRuntimeTextureSize = 1.0f / RuntimeTextureSize;
    public const int ShadowMapSize = 4096;

    public ITerrainDataProvider? DataProvider;
    public IStreamingManager? StreamingManager;
    public PhysicsManager? PhysicsManager;
    public GpuTextureManager? GpuTextureManager;
    public MeshManager? MeshManager;

    public float UnitsPerTexel => TerrainData.Header.UnitsPerTexel;
    public int MaximumLod;
    public int MinimumLod;
    public int ChunksPerRowLod0;

    public RenderModel? RenderModel;
    public ModelComponent? ModelComponent;

    public string? TerrainDataUrl;
    public TerrainData TerrainData;
    public bool IsInitialized;

    public int ShadowBlurRadius;
    public float ShadowBlurSigmaRatio;

    public (Vector2 Uv, int Lod) GetAtlasUv(float wx, float wz)
    {
        var invUnitsPerTexel = 1.0f / TerrainData.Header.UnitsPerTexel;

        wx = wx * invUnitsPerTexel;
        wz = wz * invUnitsPerTexel;

        var chunkSize = TerrainData.Header.ChunkSize;

        var sectorX = Math.Clamp((int)Math.Floor(wx / chunkSize), 0, ChunksPerRowLod0 - 1);
        var sectorZ = Math.Clamp((int)Math.Floor(wz / chunkSize), 0, ChunksPerRowLod0 - 1);
        var sectorIndex = sectorZ * ChunksPerRowLod0 + sectorX;

        var chunkIndex = MeshManager!.SectorToChunkMap[sectorIndex];

        var lodLevel = MeshManager.ChunkData[chunkIndex].Data0 & 0xFF;
        var scale = 1 << lodLevel;

        var uv = UnpackInt2(MeshManager.ChunkData[chunkIndex].PackedUv);

        var positionInChunkX = ((float)wx / scale) % chunkSize;
        var positionInChunkZ = ((float)wz / scale) % chunkSize;

        uv.X += positionInChunkX + 0.5f;
        uv.Y += positionInChunkZ + 0.5f;

        uv *= InvRuntimeTextureSize;

        uv.X = Math.Clamp(uv.X, 0.0f, 0.999999f);
        uv.Y = Math.Clamp(uv.Y, 0.0f, 0.999999f);

        return (uv, lodLevel);

        static Vector2 UnpackInt2(int v)
            => new(v & 0xFFFF, (v >> 16) & 0xFFFF);
    }

    public float GetHeightAt(Vector2 uv)
    {
        var fx = uv.X * (RuntimeTextureSize - 1);
        var fy = uv.Y * (RuntimeTextureSize - 1);

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var x1 = Math.Min(x0 + 1, RuntimeTextureSize - 1);
        var y1 = Math.Min(y0 + 1, RuntimeTextureSize - 1);

        var tx = fx - x0;
        var ty = fy - y0;

        var h00 = GetHeightAt(x0, y0);
        var h10 = GetHeightAt(x1, y0);
        var h01 = GetHeightAt(x0, y1);
        var h11 = GetHeightAt(x1, y1);

        var hx0 = MathUtil.Lerp(h00, h10, tx);
        var hx1 = MathUtil.Lerp(h01, h11, tx);
        return MathUtil.Lerp(hx0, hx1, ty);
    }

    public float GetHeightAt(int x, int y)
    {
        var height = GpuTextureManager!.ReadHeight(x, y);
        return height * TerrainData.Header.MaxHeight;
    }

    public uint GetControlMapAt(Vector2 uv)
    {
        var x = Math.Clamp((int)(uv.X * RuntimeTextureSize), 0, RuntimeTextureSize - 1);
        var y = Math.Clamp((int)(uv.Y * RuntimeTextureSize), 0, RuntimeTextureSize - 1);
        return GpuTextureManager!.ReadControlMap(x, y);
    }

    public (uint A, uint B, uint C, uint D) GetControlMapLinearSampleAt(Vector2 uv)
    {
        var fx = uv.X * (RuntimeTextureSize - 1);
        var fy = uv.Y * (RuntimeTextureSize - 1);

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var x1 = Math.Min(x0 + 1, RuntimeTextureSize - 1);
        var y1 = Math.Min(y0 + 1, RuntimeTextureSize - 1);

        var tx = fx - x0;
        var ty = fy - y0;

        var a = GpuTextureManager!.ReadControlMap(x0, y0);
        var b = GpuTextureManager!.ReadControlMap(x1, y0);
        var c = GpuTextureManager!.ReadControlMap(x0, y1);
        var d = GpuTextureManager!.ReadControlMap(x1, y1);

        return (a, b, c, d);
    }

    public void Dispose()
    {
        RenderModel = null;

        ModelComponent?.Entity?.Remove(ModelComponent);
        ModelComponent = null;

        StreamingManager?.Dispose();
        StreamingManager = null;

        PhysicsManager?.Dispose();
        PhysicsManager = null;

        GpuTextureManager?.Dispose();
        GpuTextureManager = null;
    }
}

public enum ChunkType
{
    Heightmap,
    NormalMap
}
