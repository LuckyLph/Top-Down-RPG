/// <summary>
/// Mob state while its target is within melee reach: stands still facing the target and starts an attack whenever the
/// damage dealer's cooldown allows (Docs/Animation.md, Phase 7). A running attack plays out its windup, contact and
/// recovery toward the direction it locked, the mob holding still and deciding nothing meanwhile; then it leaves for
/// Chase once the target moved past the exit buffer, and for Search or Return when the target is lost.
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
    /// Waits while an attack runs. Then goes to Search when the target is lost but hidden within the lose distance, to
    /// Return when it is lost otherwise, gives up at the leash when the target or the mob is beyond it, and goes to
    /// Chase when the target left attack range; else attacks.
    /// </summary>
    public override void Tick()
    {
        if (DamageDealer.IsAttacking)
        {
            return;
        }

        if (!Perception.HasDetectedTarget || Perception.CurrentTarget == null)
        {
            Brain.ChangeState(Perception.IsTargetHiddenInRange ? MobStateId.Search : MobStateId.Return);
            return;
        }

        if (!IsWithinLeash(Perception.CurrentTarget.position))
        {
            GiveUpAtLeash();
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
    /// Cancels an attack still running when the state is left from outside (a rebuild), since its transitions wait for
    /// it.
    /// </summary>
    public override void Exit()
    {
        DamageDealer.CancelAttack();
    }

    /// <summary>
    /// Faces the target and starts an attack on it when the damage dealer's cooldown allows.
    /// </summary>
    private void TryAttackCurrentTarget()
    {
        if (Perception.CurrentTarget == null)
        {
            return;
        }

        Motor.FaceTowards(Perception.CurrentTarget.position);
        DamageDealer.TryStartAttack(Perception.CurrentTarget);
    }
}
