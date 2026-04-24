public class IdleState : MobStateBase
{
    private float idleTimer;

    public IdleState(MobBrain brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Idle;

    public override void Enter()
    {
        PathAgent.ClearPath();
        Motor.Stop();
        idleTimer = Patrol.GetIdleDuration();
    }

    public override void Tick()
    {
        if (Perception.HasDetectedTarget)
        {
            Brain.ChangeState(MobStateId.Chase);
            return;
        }

        idleTimer -= Brain.DeltaTime;
        if (idleTimer <= 0f)
        {
            Brain.ChangeState(MobStateId.Patrol);
        }
    }

    public override void FixedTick()
    {
        Motor.Stop();
    }
}
