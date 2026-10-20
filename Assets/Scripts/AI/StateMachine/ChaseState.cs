using UnityEngine;

/// <summary>
/// Mob state that paths toward its detected target, repathing every <see cref="MobConfig.RepathInterval"/>, and waits
/// behind allies crowding the target instead of pushing through them.
/// </summary>
public class ChaseState : MobStateBase
{
    private const float WaitReleaseFactor = MobSeparation2D.SenseRadiusFactor;

    private float repathTimer;
    private bool waitingBehindCrowd;

    /// <summary>
    /// Creates the chase state for the mob run by <paramref name="brain"/>.
    /// </summary>
    public ChaseState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Chase;

    /// <summary>
    /// Resets the repath timer and the crowd wait and clears the path, so the first tick builds a fresh path.
    /// </summary>
    public override void Enter()
    {
        repathTimer = 0f;
        waitingBehindCrowd = false;
        PathAgent.ClearPath();
    }

    /// <summary>
    /// Hands over to Search or Return when the target is lost, gives up at the leash
    /// (<see cref="MobStateBase.GiveUpAtLeash"/>) when the target or the mob is beyond it, and goes to AttackRange when
    /// the target is in reach. Otherwise, unless waiting behind the crowd, repaths when due, goes to Return when the
    /// target is unreachable or only partially reachable, and gives up at the leash when the path leaves it.
    /// </summary>
    public override void Tick()
    {
        if (!Perception.HasDetectedTarget || Perception.CurrentTarget == null)
        {
            Brain.ChangeState(Perception.IsTargetHiddenInRange ? MobStateId.Search : MobStateId.Return);
            return;
        }

        if (!IsWithinLeash(Perception.CurrentTarget.position))
        {
            GiveUpAtLeash();
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
            return;
        }

        if (!PathAgent.HasGoalCell || repathTimer <= 0f)
        {
            Vector2 targetPosition = Perception.CurrentTarget.position;

            if (!PathAgent.CanReachWorldTarget(targetPosition))
            {
                Brain.ChangeState(MobStateId.Return);
                return;
            }

            bool pathFound = PathAgent.BuildPathToWorld(targetPosition, allowPartial: true);
            repathTimer = Config.RepathInterval;

            if (!pathFound || !PathAgent.ReachedResolvedGoal || PathAgent.GoalWasAdjusted)
            {
                Brain.ChangeState(MobStateId.Return);
            }
            else if (!Patrol.IsPathWithinLeash(PathAgent.Waypoints))
            {
                GiveUpAtLeash();
            }
        }
    }

    /// <summary>
    /// Waits, stopped and facing the target, while an ally stands ahead near the target (with a wider range to stop
    /// waiting than to start); otherwise follows the path.
    /// </summary>
    public override void FixedTick()
    {
        waitingBehindCrowd = IsBlockedByCrowdNearTarget(waitingBehindCrowd ? WaitReleaseFactor : 1f);
        if (waitingBehindCrowd)
        {
            Motor.Stop();
            Motor.FaceTowards(Perception.CurrentTarget.position);
            return;
        }

        PathAgent.FixedTick();
    }

    /// <summary>
    /// Whether the mob is within the crowd wait distance of its target with a neighbour ahead of it within the
    /// separation radius, both scaled by <paramref name="rangeFactor"/>.
    /// </summary>
    private bool IsBlockedByCrowdNearTarget(float rangeFactor)
    {
        Transform target = Perception.CurrentTarget;
        if (target == null)
        {
            return false;
        }

        Vector2 targetPosition = target.position;
        return Vector2.Distance(Motor.Position, targetPosition) <= Config.CrowdWaitDistance * rangeFactor
            && Brain.Separation.HasNeighborToward(targetPosition, Config.SeparationRadius * rangeFactor);
    }
}
