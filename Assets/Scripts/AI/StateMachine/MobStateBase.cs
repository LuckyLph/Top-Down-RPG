using UnityEngine;

/// <summary>
/// Base for mob states: holds the brain and exposes its components and config, with empty defaults for every
/// callback.
/// </summary>
public abstract class MobStateBase : IMobState
{
    protected readonly MobController Brain;

    /// <summary>
    /// Captures the brain whose components and config the state works through.
    /// </summary>
    protected MobStateBase(MobController brain)
    {
        Brain = brain;
    }

    protected MobConfig Config => Brain.Config;
    protected MobPerception2D Perception => Brain.Perception;
    protected MobPathAgent2D PathAgent => Brain.PathAgent;
    protected MobMotor2D Motor => Brain.Motor;
    protected MobPatrolAnchor Patrol => Brain.Patrol;
    protected MeleeDamageDealer DamageDealer => Brain.DamageDealer;
    protected Transform Transform => Brain.transform;

    public abstract MobStateId StateId { get; }

    /// <summary>
    /// Whether the mob spots a target worth chasing: a detected target inside the leash, with the mob inside it too,
    /// that it can path to, unless the mob gave up at its leash and has not idled since. What every state but Chase and
    /// AttackRange checks before chasing.
    /// </summary>
    protected bool SpotsTarget()
    {
        Transform target = Perception.CurrentTarget;
        return !Patrol.IsLeashed && Perception.HasDetectedTarget && target != null && IsWithinLeash(target.position)
            && PathAgent.CanReachWorldTarget(target.position);
    }

    /// <summary>
    /// Gives up on the target at the leash: marks the mob leashed and sends it home.
    /// </summary>
    protected void GiveUpAtLeash()
    {
        Patrol.SetLeashed(true);
        Brain.ChangeState(MobStateId.Return);
    }

    /// <summary>
    /// Whether <paramref name="position"/> and the mob itself both lie within its leash; always true for a mob without
    /// a home chunk.
    /// </summary>
    protected bool IsWithinLeash(Vector2 position)
    {
        return Patrol.IsWithinLeash(position) && Patrol.IsWithinLeash(Motor.Position);
    }

    /// <summary>
    /// Does nothing; override to act when the brain switches to this state.
    /// </summary>
    public virtual void Enter() { }
    /// <summary>
    /// Does nothing; override to make per-frame decisions.
    /// </summary>
    public virtual void Tick() { }
    /// <summary>
    /// Does nothing; override to drive movement each physics step.
    /// </summary>
    public virtual void FixedTick() { }
    /// <summary>
    /// Does nothing; override to clean up when the brain leaves this state.
    /// </summary>
    public virtual void Exit() { }
}
