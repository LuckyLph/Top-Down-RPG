// Entered when a chased target slips out of sight without leaving range (e.g. around a wall): walk to
// where it was last seen and wait there, resuming the chase if it comes back into view anywhere within
// loseTargetDistance, and give up once searchDuration runs out.
public class SearchState : MobStateBase
{
    private float searchTimer;

    public SearchState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Search;

    public override void Enter()
    {
        searchTimer = Config.searchDuration;
        Perception.IsAlert = true;
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

        searchTimer -= Brain.DeltaTime;
        if (searchTimer <= 0f)
        {
            Brain.ChangeState(MobStateId.Return);
        }
    }

    public override void Exit()
    {
        Perception.IsAlert = false;
    }

    public override void FixedTick()
    {
        // Once there the agent stops, so the mob waits and keeps looking until the timer runs out.
        PathAgent.FixedTick();
    }
}
