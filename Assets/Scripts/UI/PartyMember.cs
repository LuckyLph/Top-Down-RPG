/// <summary>
/// A player <see cref="PartyRoster"/> follows: its handle, slot and statuses, and when this machine saw it die.
/// </summary>
public sealed class PartyMember
{
    /// <summary>
    /// Follows <paramref name="handle"/> with its <paramref name="slot"/> and <paramref name="statuses"/>.
    /// </summary>
    public PartyMember(PlayerHandle handle, PlayerSlot slot, StatusEffects statuses)
    {
        Handle = handle;
        Slot = slot;
        Statuses = statuses;
    }

    public PlayerHandle Handle { get; }
    public PlayerSlot Slot { get; }
    public StatusEffects Statuses { get; }
    public bool Dead { get; private set; }
    public float DiedAt { get; private set; }

    /// <summary>
    /// The player's health as a fraction of its maximum; 0 without health.
    /// </summary>
    public float HealthFraction
    {
        get
        {
            Health health = Handle.Health;
            return health != null && health.MaxHealth > 0 ? (float)health.CurrentHealth / health.MaxHealth : 0f;
        }
    }

    /// <summary>
    /// Records the time <paramref name="now"/> as it is first seen dead, and forgets it once it lives again.
    /// </summary>
    public void Track(float now)
    {
        bool dead = Handle.Health != null && Handle.Health.IsDead;
        if (dead && !Dead)
        {
            DiedAt = now;
        }

        Dead = dead;
    }
}
