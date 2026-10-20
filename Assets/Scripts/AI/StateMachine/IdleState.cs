/// <summary>
/// Mob state that stands still for a random idle duration, then patrols; chases a detected target it can reach.
/// </summary>
public class IdleState : MobStateBase
{
    private float idleTimer;

    /// <summary>
    /// Creates the idle state for the mob run by <paramref name="brain"/>.
    /// </summary>
    public IdleState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Idle;

    /// <summary>
    /// Clears the path, stops and rolls the idle duration.
    /// </summary>
    public override void Enter()
    {
        PathAgent.ClearPath();
        Motor.Stop();
        idleTimer = Patrol.GetIdleDuration();
    }

    /// <summary>
    /// Goes to Chase when it spots a target within its leash that it can reach, else counts the idle time down, then
    /// lets go of the leash (a mob that gave up at it spots targets again) and goes to Patrol.
    /// </summary>
    public override void Tick()
    {
        if (SpotsTarget())
        {
            Brain.ChangeState(MobStateId.Chase);
            return;
        }

        idleTimer -= Brain.DeltaTime;
        if (idleTimer <= 0f)
        {
            Patrol.SetLeashed(false);
            Brain.ChangeState(MobStateId.Patrol);
        }
    }

    /// <summary>
    /// Keeps the mob stopped.
    /// </summary>
    public override void FixedTick()
    {
        Motor.Stop();
    }
}
