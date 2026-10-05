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
