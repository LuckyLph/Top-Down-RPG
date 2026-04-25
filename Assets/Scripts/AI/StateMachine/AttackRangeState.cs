public class AttackRangeState : MobStateBase
{
    public AttackRangeState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.AttackRange;

    public override void Enter()
    {
        // Dummy attack state for now: stop movement and hold orientation toward target.
        PathAgent.ClearPath();
        Motor.Stop();
        Brain.InvokeAttackRangeEntered();
    }

    public override void Tick()
    {
        if (!Perception.HasDetectedTarget || Perception.CurrentTarget == null)
        {
            Brain.ChangeState(MobStateId.Return);
            return;
        }

        Motor.FaceTowards(Perception.CurrentTarget.position);

        if (Brain.ShouldExitAttackRange())
        {
            Brain.ChangeState(MobStateId.Chase);
        }
    }

    public override void FixedTick()
    {
        Motor.Stop();
    }
}
