public class SearchState : MobStateBase
{
    private float searchTimer;

    public SearchState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Search;

    public override void Enter()
    {
        searchTimer = Config.searchDuration;
        if (!PathAgent.BuildPathToWorld(Perception.LastKnownTargetPosition, allowPartial: false))
        {
            Brain.ChangeState(MobStateId.Return);
        }
    }

    public override void Tick()
    {
        if (Perception.HasDetectedTarget && Perception.CurrentTarget != null && PathAgent.CanReachWorldTarget(Perception.CurrentTarget.position))
        {
            Brain.ChangeState(MobStateId.Chase);
            return;
        }

        if (!HasArrived())
        {
            return;
        }

        searchTimer -= Brain.DeltaTime;
        if (searchTimer <= 0f)
        {
            Brain.ChangeState(MobStateId.Return);
        }
    }

    public override void FixedTick()
    {
        PathAgent.FixedTick();
    }

    private bool HasArrived()
    {
        return PathAgent.ReachedDestination || PathAgent.StalledTime >= Config.stuckTimeout;
    }
}
