using UnityEngine;

public class AttackRangeState : MobStateBase
{
    public AttackRangeState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.AttackRange;

    public override void Enter()
    {
        PathAgent.ClearPath();
        Motor.Stop();
        Brain.InvokeAttackRangeEntered();
        TryAttackCurrentTarget();
    }

    public override void Tick()
    {
        if (!Perception.HasDetectedTarget || Perception.CurrentTarget == null)
        {
            Brain.ChangeState(Perception.IsTargetHiddenInRange ? MobStateId.Search : MobStateId.Return);
            return;
        }

        if (Brain.ShouldExitAttackRange())
        {
            Brain.ChangeState(MobStateId.Chase);
            return;
        }

        TryAttackCurrentTarget();
    }

    public override void FixedTick()
    {
        Motor.Stop();
    }

    private void TryAttackCurrentTarget()
    {
        if (Perception.CurrentTarget == null)
        {
            return;
        }

        Motor.FaceTowards(Perception.CurrentTarget.position);

        Vector2 attackDirection = Perception.CurrentTarget.position - Brain.transform.position;
        if (DamageDealer.TryDealDamage(Perception.CurrentTarget))
        {
            Motor.PlayAttackAnimation(attackDirection);
        }
    }
}
