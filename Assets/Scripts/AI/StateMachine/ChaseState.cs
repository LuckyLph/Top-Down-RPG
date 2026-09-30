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

        // Re-plan from where the mob actually is every interval, even if the target's cell is unchanged:
        // collisions can push the mob off its path, leaving its old waypoints behind a wall.
        if (!PathAgent.HasGoalCell || repathTimer <= 0f)
        {
            bool pathFound = PathAgent.BuildPathToWorld(Perception.CurrentTarget.position, allowPartial: true);
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
