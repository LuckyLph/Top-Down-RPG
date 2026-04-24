using UnityEngine;

public class PatrolRoamState : MobStateBase
{
    public PatrolRoamState(MobBrain brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Patrol;

    public override void Enter()
    {
        if (!Patrol.TryGetRoamDestination(out Vector2 destination))
        {
            Brain.ChangeState(MobStateId.Idle);
            return;
        }

        bool pathFound = PathAgent.BuildPathToWorld(destination, allowPartial: true);
        if (!pathFound)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    public override void Tick()
    {
        if (Perception.HasDetectedTarget)
        {
            Brain.ChangeState(MobStateId.Chase);
            return;
        }

        if (PathAgent.ReachedDestination)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    public override void FixedTick()
    {
        PathAgent.FixedTick();
    }
}
