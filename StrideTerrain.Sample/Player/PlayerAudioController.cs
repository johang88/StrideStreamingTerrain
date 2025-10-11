using Stride.Audio;
using Stride.Core;
using Stride.Engine;
using Stride.Engine.Events;
using Stride.Media;
using StrideTerrain.TerrainSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrideTerrain.Sample.Player;

public class PlayerAudioController : SyncScript
{
    public Dictionary<GroundType, FootstepsSounds> Footsteps { get; set; } = [];
    private SoundInstance? _activeInstance;

    private readonly EventReceiver<float> runSpeedEvent = new EventReceiver<float>(PlayerController.RunSpeedEventKey);

    public override void Start()
    {
        base.Start();

        foreach (var f in Footsteps.Values)
        {
            f.Instances = [..f.Sounds.Select(x =>
            {
                var instance = x.CreateInstance();
                instance.IsLooping = false;
                return instance;
            })];
        }
    }

    public override void Cancel()
    {
        base.Cancel();


        foreach (var f in Footsteps.Values)
        {
            foreach (var instance in f.Instances)
            {
                instance.Dispose();
            }
            f.Instances.Clear();
        }
    }

    public override void Update()
    {
        if (!runSpeedEvent.TryReceive(out var speed) || speed <= 0.01f)
            return;

        if (_activeInstance != null && _activeInstance.PlayState == PlayState.Playing)
            return;

        var terrainProcessor = SceneSystem.SceneInstance.Processors.Get<TerrainProcessor>();
        if (terrainProcessor?.TerrainData?.MeshManager?.IsReady != true)
            return;

        var terrainData = terrainProcessor.TerrainData;
        if (!terrainData.IsInitialized)
            return;

        Entity.Transform.GetWorldTransformation(out var positionWorld, out var _, out var _);

        var (uv, _) = terrainData.GetAtlasUv(positionWorld.X, positionWorld.Z);
        var controlValue = terrainData.GetControlMapAt(uv);

        var groundType = GetGroundType(controlValue & 0x1F);

        if (!Footsteps.TryGetValue(groundType, out var sounds))
            return;

        var instances = sounds.Instances;
        _activeInstance = instances[Random.Shared.Next(instances.Count)];
        _activeInstance.Play();
    }

    private GroundType GetGroundType(uint textureIndex)
    {
        return textureIndex switch
        {
            21 => GroundType.Mud,
            4 => GroundType.Mud,
            3 => GroundType.Mud,
            12 => GroundType.Snow,
            14 => GroundType.Dirt, // Don't have rock sounds atm
            19 => GroundType.Dirt,
            _ => GroundType.Grass,
        };
    }
}

[DataContract]
public class FootstepsSounds
{
    public List<Sound> Sounds { get; set; } = [];
    [DataMemberIgnore]
    public List<SoundInstance> Instances { get; set; } = [];
}

public enum GroundType
{
    Grass,
    Snow,
    Mud,
    Dirt
}
