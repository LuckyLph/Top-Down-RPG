/// <summary>
/// What <see cref="PlayerOrders"/> needs from the player's abilities: the slots, readiness, and starting,
/// completing, cancelling and reporting casts. Whether a cast is already running is decided by the orders.
/// </summary>
public interface IAbilityCaster
{
    AbilityDefinition GetAbility(int slot);

    /// <summary>
    /// False with the reason when the slot is empty, the ability is on cooldown or the caster is dead.
    /// </summary>
    bool CanCast(int slot, out CastOutcome failure);

    /// <summary>
    /// Starts the cast (cooldown, events) and returns <see cref="CastOutcome.Started"/>, or reports and returns why
    /// it cannot.
    /// </summary>
    CastOutcome TryCast(int slot, in CastAim aim);

    /// <summary>
    /// Ends the running cast because its cast time ran out: its effect lands.
    /// </summary>
    void CompleteCast(int slot);

    /// <summary>
    /// Ends the running cast early (death, a stun or silence, a teleport): no effect, the cooldown stays spent.
    /// </summary>
    void CancelCast(int slot);

    void ReportFailure(int slot, CastOutcome reason);
}
