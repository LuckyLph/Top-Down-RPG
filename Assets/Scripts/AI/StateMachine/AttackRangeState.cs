public class AttackRangeState : MobStateBase
{
    public AttackRangeState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.AttackRange;

    public override void Enter()
    {
        PathAgent.ClearPath();
        Motor.Stop();
        DamageDealer.ResetCooldown();
        Brain.InvokeAttackRangeEntered();

        if (Perception.CurrentTarget != null)
        {
            Motor.FaceTowards(Perception.CurrentTarget.position);
            DamageDealer.TryDealDamage(Perception.CurrentTarget);
        }
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
            return;
        }

        DamageDealer.TryDealDamage(Perception.CurrentTarget);
    }

    public override void FixedTick()
    {
        Motor.Stop();
    }
}
