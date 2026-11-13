/// <summary>
/// The words the ability bar's message line shows for a failed press (Docs/UiRevamp.md, Decisions: Failures), one per
/// <see cref="CastOutcome"/> a player can act on. An empty slot, a dead player, and a buffered cast that expired or was
/// dropped show none: the slot is empty, the death notice explains the rest, and a dropped cast only loses its
/// brackets. The texts are constants, so showing one never allocates.
/// </summary>
public static class CastFailureMessages
{
    public const string OnCooldown = "On cooldown";
    public const string AlreadyCasting = "Already casting";
    public const string NoTarget = "No target";
    public const string Stunned = "Stunned";
    public const string Silenced = "Silenced";
    public const string Rooted = "Rooted";

    /// <summary>
    /// The text the message line shows for <paramref name="outcome"/>, or null when it shows none.
    /// </summary>
    public static string For(CastOutcome outcome)
    {
        return outcome switch
        {
            CastOutcome.OnCooldown => OnCooldown,
            CastOutcome.CastInProgress => AlreadyCasting,
            CastOutcome.NoTarget => NoTarget,
            CastOutcome.Stunned => Stunned,
            CastOutcome.Silenced => Silenced,
            CastOutcome.Rooted => Rooted,
            _ => null
        };
    }
}
