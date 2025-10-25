using Stride.Animations;
using Stride.Core;
using Stride.Engine;
using System;

namespace StrideTerrain.Weather;

public class TimeOfDayComponent : SyncScript
{
    private float _timeOfDay;
    public float TimeOfDay
    {
        get { return _timeOfDay; }
        set
        {
            _timeOfDay = value % 1.0f;
            if (_timeOfDay < 0.0f) 
                _timeOfDay += 1.0f;
        }
    }

    public float TimeOfDaySpeed { get; set; } = 1.0f;

    [DataMemberIgnore]
    public float SnappedTimeOfDay
    {
        get
        {
            if (TimeOfDaySpeed > 5.0f)
                return TimeOfDay;
            
            var steps = 360;
            var stepSize = 1.0f / steps;
            return MathF.Floor(TimeOfDay / stepSize) * stepSize;
        }
    }

    [DataMemberIgnore]
    public float SunAngle => (SnappedTimeOfDay * 360.0f) + 90.0f;

    public IComputeCurve<float>? ExposureAutoKeyBiasPower { get; set; }

    public override void Update()
    {
        var deltaTime = (float)Game.UpdateTime.Elapsed.TotalSeconds;
        TimeOfDay = (TimeOfDay + (deltaTime / 3600.0f) * TimeOfDaySpeed) % 1.0f;
    }
}
