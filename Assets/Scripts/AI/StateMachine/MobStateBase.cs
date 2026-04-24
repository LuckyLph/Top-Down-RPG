using UnityEngine;

public abstract class MobStateBase : IMobState
{
    protected readonly MobBrain Brain;

    protected MobStateBase(MobBrain brain)
    {
        Brain = brain;
    }

    protected MobConfig Config => Brain.Config;
    protected MobPerception2D Perception => Brain.Perception;
    protected MobPathAgent2D PathAgent => Brain.PathAgent;
    protected MobMotor2D Motor => Brain.Motor;
    protected MobPatrolRoam Patrol => Brain.Patrol;
    protected Transform Transform => Brain.transform;

    public abstract MobStateId StateId { get; }

    public virtual void Enter() { }
    public virtual void Tick() { }
    public virtual void FixedTick() { }
    public virtual void Exit() { }
}
