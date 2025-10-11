using Stride.Audio;
using Stride.Core;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Engine;
using Stride.Media;
using StrideTerrain.Common;
using StrideTerrain.TerrainSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrideTerrain.Audio;

public class AmbientSoundManager : SyncScript
{
    private const float VolumeSmooth = 0.1f;

    public required UrlReference BiomeData { get; set; }
    public Dictionary<BiomeType, BiomeSounds> Biomes { get; set; } = [];

    private byte[] _biomes = null!;
    private List<BiomeInstance> _instances = [];
    private float[] _biomeWeights = [];

    public override void Start()
    {
        base.Start();

        using var stream = Content.OpenAsStream(BiomeData.Url, StreamFlags.None);
        _biomes = new byte[stream.Length];
        stream.ReadAtLeast(_biomes.AsSpan(), _biomes.Length);

        // Create instances for all biomes + sounds
        _instances = [.. Enumerable.Range(0, (int)BiomeType.Last + 1).Select(x => new BiomeInstance
        {
            Type = (BiomeType)x
        })];

        foreach (var biome in Biomes)
        {
            foreach (var sound in biome.Value.Sounds)
            {
                var instance = sound.CreateInstance();
                instance.IsLooping = false;
                _instances[(int)biome.Key].Sounds.Add(instance);
            }
        }

        _biomeWeights = new float[_instances.Count];
    }

    public override void Cancel()
    {
        base.Cancel();

        foreach (var instance in _instances.SelectMany(x => x.Sounds))
        {
            instance.Dispose();
        }
        _instances.Clear();
    }

    public override void Update()
    {
        var terrainProcessor = SceneSystem.SceneInstance.Processors.Get<TerrainProcessor>();
        if (terrainProcessor?.TerrainData?.MeshManager?.IsReady != true)
            return;

        var terrainData = terrainProcessor.TerrainData;
        if (!terrainData.IsInitialized)
            return;

        var size = (int)Math.Sqrt(_biomes.Length);
        float terrainToBiome = (float)size / terrainData.TerrainData.Header.Size;

        Vector2 playerPosWorld = Entity.Transform.Position.XZ();
        Vector2 playerPosTerrain = playerPosWorld / terrainData.TerrainData.Header.UnitsPerTexel;
        Vector2 playerPosBiome = playerPosTerrain * terrainToBiome;

        int px = Math.Clamp((int)playerPosBiome.X, 0, size - 1);
        int py = Math.Clamp((int)playerPosBiome.Y, 0, size - 1);

        int radius = 3; // neighborhood radius for blending

        float totalWeight = 0f;
        for (int y = Math.Max(0, py - radius); y <= Math.Min(size - 1, py + radius); y++)
        {
            for (int x = Math.Max(0, px - radius); x <= Math.Min(size - 1, px + radius); x++)
            {
                var biome = (BiomeType)_biomes[y * size + x];

                float dx = x - playerPosBiome.X;
                float dy = y - playerPosBiome.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                float weight = MathF.Max(0f, 1f - dist / (radius + 1));
                _biomeWeights[(int)biome] += weight;
                totalWeight += weight;
            }
        }

        if (totalWeight > 0f)
        {
            for (int i = 0; i < _biomeWeights.Length; i++)
                _biomeWeights[i] /= totalWeight;
        }

        // Apply smooth volume blending
        for (int i = 0; i < _instances.Count; i++)
        {
            var instance = _instances[i];
            float targetVolume = _biomeWeights[i];

            if (targetVolume > 0f)
            {
                instance.Play(targetVolume);
            }
            else
            {
                instance.FadeOut();
            }
        }
    }

    private class BiomeInstance
    {
        public List<SoundInstance> Sounds = [];
        public SoundInstance? ActiveSound;
        public BiomeType Type;

        public void Play(float targetVolume)
        {
            if (ActiveSound == null || ActiveSound.PlayState != PlayState.Playing)
            {
                ActiveSound?.Stop();
                
                ActiveSound = Sounds[Random.Shared.Next(Sounds.Count)];
                ActiveSound.Volume = 0;
                
                ActiveSound.Play();

                ActiveSound.Pitch = GetPitchForBiome(Type);
            }

            ActiveSound.Volume = MathUtil.Lerp(ActiveSound.Volume,targetVolume * 0.25f, VolumeSmooth);
        }

        public void FadeOut()
        {
            if (ActiveSound != null)
            {
                ActiveSound.Volume = MathUtil.Lerp(ActiveSound.Volume, 0.0f, VolumeSmooth);
                if (ActiveSound.Volume < 0.01f)
                {
                    ActiveSound.Stop();
                    ActiveSound = null;
                }
            }
        }

        private static float GetPitchForBiome(BiomeType biome)
        {
            float basePitch = 1.0f;
            float variation = 0.05f; // ±5%
            switch (biome)
            {
                case BiomeType.Forest: basePitch = 0.95f + (float)Random.Shared.NextDouble() * variation; break;
                case BiomeType.Plains: basePitch = 0.97f + (float)Random.Shared.NextDouble() * variation; break;
                case BiomeType.Mountain: basePitch = 0.9f + (float)Random.Shared.NextDouble() * variation; break;
                case BiomeType.Beach: basePitch = 0.98f + (float)Random.Shared.NextDouble() * variation; break;
                case BiomeType.Ocean: basePitch = 1.0f + (float)Random.Shared.NextDouble() * variation; break;
            }
            return basePitch;
        }
    }
}

[DataContract]
public class BiomeSounds
{
    public List<Sound> Sounds { get; set; } = [];
}
