using UnityEngine;

public class ChaseState : MobStateBase
{
    private const float WaitReleaseFactor = MobSeparation2D.SenseRadiusFactor;

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
            repathTimer = Config.repathInterval;

            if (!pathFound || !PathAgent.ReachedResolvedGoal || PathAgent.GoalWasAdjusted)
            {
                Brain.ChangeState(MobStateId.Return);
            }
        }
    }

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

    private bool IsBlockedByCrowdNearTarget(float rangeFactor)
    {
        Transform target = Perception.CurrentTarget;
        if (target == null)
        {
            return false;
        }

        Vector2 targetPosition = target.position;
        return Vector2.Distance(Motor.Position, targetPosition) <= Config.crowdWaitDistance * rangeFactor
            && Brain.Separation.HasNeighborToward(targetPosition, Config.separationRadius * rangeFactor);
    }
}
