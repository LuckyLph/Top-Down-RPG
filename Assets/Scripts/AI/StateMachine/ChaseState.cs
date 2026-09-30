using UnityEngine;

public class ChaseState : MobStateBase
{
    private float repathTimer;
    private bool waitingBehindCrowd;

    public ChaseState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Chase;

    public override void Enter()
    {
        repathTimer = 0f;
        waitingBehindCrowd = false;
        PathAgent.ClearPath();
    }

    public override void Tick()
    {
        if (!Perception.HasDetectedTarget || Perception.CurrentTarget == null)
        {
            Brain.ChangeState(Perception.IsTargetHiddenInRange ? MobStateId.Search : MobStateId.Return);
            return;
        }

        if (Brain.IsTargetInAttackRange())
        {
            Brain.ChangeState(MobStateId.AttackRange);
            return;
        }

        repathTimer -= Brain.DeltaTime;
        if (waitingBehindCrowd)
        {
            // Not moving, so there is nothing to re-plan until the way clears.
            return;
        }

        // Re-plan from where the mob actually is every interval, even if the target's cell is unchanged:
        // collisions can push the mob off its path, leaving its old waypoints behind a wall.
        if (!PathAgent.HasGoalCell || repathTimer <= 0f)
        {
            Vector2 targetPosition = Perception.CurrentTarget.position;

            // Cached connectivity rules out an unreachable target without an A* search across the region.
            if (!PathAgent.CanReachWorldTarget(targetPosition))
            {
                Brain.ChangeState(MobStateId.Return);
                return;
            }

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
        waitingBehindCrowd = IsBlockedByCrowdNearTarget();
        if (waitingBehindCrowd)
        {
            // Hold position facing the target; separation still keeps this mob off its neighbors.
            Motor.Stop();
            Motor.FaceTowards(Perception.CurrentTarget.position);
            return;
        }

        PathAgent.FixedTick();
    }

    private bool IsBlockedByCrowdNearTarget()
    {
        Transform target = Perception.CurrentTarget;
        if (target == null)
        {
            return false;
        }

        Vector2 targetPosition = target.position;
        return Vector2.Distance(Motor.Position, targetPosition) <= Config.crowdWaitDistance
            && Brain.Separation.HasNeighborToward(targetPosition);
    }
}
