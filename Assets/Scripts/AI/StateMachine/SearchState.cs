/// <summary>
/// Mob state that walks to where its target was last seen, waits there for <see cref="MobConfig.SearchDuration"/>,
/// then returns home; chases a detected target it can reach.
/// </summary>
public class SearchState : MobStateBase
{
    private float searchTimer;

    /// <summary>
    /// Creates the search state for the mob run by <paramref name="brain"/>.
    /// </summary>
    public SearchState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Search;

    /// <summary>
    /// Starts the search timer and builds a strict (not partial) path to the last known target position; goes to
    /// Return when there is none.
    /// </summary>
    public override void Enter()
    {
        searchTimer = Config.SearchDuration;
        if (!PathAgent.BuildPathToWorld(Perception.LastKnownTargetPosition, allowPartial: false))
        {
            Brain.ChangeState(MobStateId.Return);
        }
    }

    /// <summary>
    /// Goes to Chase when a detected target is reachable; once arrived or stalled, counts the search time down and
    /// then goes to Return.
    /// </summary>
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

    /// <summary>
    /// Follows the path.
    /// </summary>
    public override void FixedTick()
    {
        PathAgent.FixedTick();
    }

    /// <summary>
    /// Whether the mob reached the last known position or has stalled for <see cref="MobConfig.StuckTimeout"/> on the
    /// way.
    /// </summary>
    private bool HasArrived()
    {
        return PathAgent.ReachedDestination || PathAgent.StalledTime >= Config.StuckTimeout;
    }
}
