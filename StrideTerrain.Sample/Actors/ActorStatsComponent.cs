using Stride.Core;
using Stride.Engine;

namespace StrideTerrain.Sample.Actors;

public class ActorStatsComponent : StartupScript
{
    [DataMember] public ActorStat<float> Health = new(100, 100);
    [DataMember] public float MovementSpeed = 5;
    public Faction Faction { get; set; }

    public bool IsAlive => Health.Current > 0;

    /// <summary>
    /// Returns true if this character is friendly towards another character
    /// </summary>
    public bool IsFriendly(ActorStatsComponent other)
        => other.Faction == Faction;
}

[DataContract]
public struct ActorStat<T>(T current, T max)
    where T : struct
{
    [DataMember] public T Current = current;
    [DataMember] public T Max = max;
}