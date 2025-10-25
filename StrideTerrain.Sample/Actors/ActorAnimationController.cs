using Stride.Animations;
using Stride.Core;
using Stride.Engine;
using System;

namespace StrideTerrain.Sample.Actors;

public class ActorAnimationController : SyncScript
{
    private AnimationComponent Animations => Entity.Get<AnimationComponent>();

    [DataMemberIgnore] public float Speed { get; set; } = 0.0f;
    [DataMemberIgnore] public bool Hurt { get; set; }
    [DataMemberIgnore] public ActorState ActorState { get; set; }

    private AnimationState _state = AnimationState.Idle;
    private float _idleRelaxedCooldown = 0.0f;

    private PlayingAnimation? _hurtAnimation = null;

    public override void Update()
    {
        State_Any();

        var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
        if (_idleRelaxedCooldown > 0.0f)
        {
            _idleRelaxedCooldown -= dt;
        }

        switch (_state)
        {
            case AnimationState.Idle:
                State_Idle();
                break;
            case AnimationState.Walking:
                State_Walking();
                break;
            case AnimationState.Hurt:
                State_Hurt();
                break;
            case AnimationState.Running:
                State_Running();
                break;
            case AnimationState.Dying:
                State_Dying();
                break;
            case AnimationState.Dead:
                State_Dead();
                break;
        }
    }

    private void State_Idle()
    {
        if (Hurt)
            PlayAnimation("Hurt");
        else if (Speed >= 0.001f)
            SetState(AnimationState.Walking);
        else if (_idleRelaxedCooldown > 0.0f)
            PlayAnimation("Idle Ready");
        else
            PlayAnimation("Idle");
    }

    private void State_Walking()
    {
        if (Hurt)
            PlayAnimation("Hurt");
        if (Speed < 0.001f)
            SetState(AnimationState.Idle);
        else
            PlayAnimation("Walk");
    }

    private void State_Hurt()
    {
        Hurt = false;

        if (_hurtAnimation == null && Animations.Animations.ContainsKey("Hurt"))
        {
            _hurtAnimation = Animations.Crossfade("Hurt", TimeSpan.FromSeconds(0.1f)); ;
            _hurtAnimation.RepeatMode = AnimationRepeatMode.PlayOnce;
        }
        else if (_hurtAnimation == null || _hurtAnimation.CurrentTime.TotalSeconds >= _hurtAnimation.Clip.Duration.TotalSeconds)
        {
            _hurtAnimation = null;

            if (Speed < 0.001f)
                SetState(AnimationState.Idle);
            else
                SetState(AnimationState.Walking);
        }
    }

    private void State_Running()
    {
    }

    private void State_Dying()
    {
        PlayAnimation("Die");
    }

    private void State_Dead()
    {
        // NOP
    }

    private void State_Any()
    {
        if ((_state != AnimationState.Dead || _state != AnimationState.Dying) && ActorState == ActorState.Dead)
        {
            SetState(AnimationState.Dying);
        }

        // Interrupts happen only once 
        if (Hurt && (_state == AnimationState.Idle || _state == AnimationState.Walking))
        {
            SetState(AnimationState.Hurt);
        }
    }

    private void SetState(AnimationState newState)
    {
        _state = newState;
    }

    private void PlayAnimation(string name)
    {
        if (!Animations.IsPlaying(name))
        {
            Animations.Crossfade(name, TimeSpan.FromSeconds(0.3f));
        }
    }

    private enum AnimationState
    {
        Idle,
        Walking,
        Running,
        Hurt,
        Dying,
        Dead
    }
}