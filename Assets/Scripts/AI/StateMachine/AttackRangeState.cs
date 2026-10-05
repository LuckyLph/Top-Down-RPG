using UnityEngine;

/// <summary>
/// Mob state while its target is within melee reach: stands still facing the target and attacks whenever the damage
/// dealer's cooldown allows. Leaves for Chase once the target moves past the exit buffer, and for Search or Return
/// when the target is lost.
/// </summary>
public class AttackRangeState : MobStateBase
{
    /// <summary>
    /// Creates the attack state for the mob run by <paramref name="brain"/>.
    /// </summary>
    public AttackRangeState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.AttackRange;

    /// <summary>
    /// Clears the path, stops, fires the brain's attack-range event and attacks at once if the cooldown allows.
    /// </summary>
    public override void Enter()
    {
        PathAgent.ClearPath();
        Motor.Stop();
        Brain.InvokeAttackRangeEntered();
        TryAttackCurrentTarget();
    }

    /// <summary>
    /// Goes to Search when the target is lost but hidden within the lose distance, to Return when it is lost
    /// otherwise, and to Chase when it left attack range; else attacks.
    /// </summary>
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

    /// <summary>
    /// Keeps the mob stopped.
    /// </summary>
    public override void FixedTick()
    {
        Motor.Stop();
    }

    /// <summary>
    /// Faces the target and, when the damage dealer lands a hit, plays the attack animation toward it.
    /// </summary>
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
