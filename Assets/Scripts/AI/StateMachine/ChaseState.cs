using UnityEngine;

public class ChaseState : MobStateBase
{
    private float repathTimer;

    public ChaseState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Chase;

    public override void Enter()
    {
        repathTimer = 0f;
        PathAgent.ClearPath();
    }

    public override void Tick()
    {
        if (!Perception.HasDetectedTarget || Perception.CurrentTarget == null)
        {
            Brain.ChangeState(MobStateId.Return);
            return;
        }

        if (Brain.IsTargetInAttackRange())
        {
            Brain.ChangeState(MobStateId.AttackRange);
            return;
        }

        repathTimer -= Brain.DeltaTime;
        Vector2 targetPosition = Perception.CurrentTarget.position;
        bool goalCellChanged = PathAgent.IsGoalCellChanged(targetPosition);

        bool shouldBuildPath =
            !PathAgent.HasGoalCell ||
            (goalCellChanged && repathTimer <= 0f) ||
            (!PathAgent.HasPath && repathTimer <= 0f);

        if (shouldBuildPath)
        {
            bool pathFound = PathAgent.BuildPathToWorld(targetPosition, allowPartial: true);
            repathTimer = Config.repathInterval;

            if (!pathFound || !PathAgent.ReachedResolvedGoal || PathAgent.GoalWasAdjusted)
            {
                Brain.ChangeState(MobStateId.Return);
            }
        }
    }

    public override void FixedTick()
    {
        PathAgent.FixedTick();
    }
}
