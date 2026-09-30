// Entered when a chased target slips out of sight without leaving range (e.g. around a wall): walk to
// where it was last seen and wait there, resuming the chase if it comes back into view, and give up
// once it has waited searchDuration at the spot. Walking there does not use up the search time.
public class SearchState : MobStateBase
{
    // Search time left at the last known position; only counts down once the mob gets there.
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
        // Once there the agent stops, so the mob waits and keeps looking until the timer runs out.
        PathAgent.FixedTick();
    }

    // Blocked on the way (e.g. by a crowd) counts as arrived, so the search always ends.
    private bool HasArrived()
    {
        return PathAgent.ReachedDestination || PathAgent.StalledTime >= Config.stuckTimeout;
    }
}
