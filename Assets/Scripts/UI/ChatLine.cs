/// <summary>
/// One line of the chat on this machine (Docs/UiRevamp.md, Phase 12): a player's message, which names the sender's
/// slot, or a line about the party, which names none; its text; the game time it arrived here, which its fading counts
/// from; and the time of day it arrived, "14:32", which the chat shows with timestamps on.
/// </summary>
public readonly struct ChatLine
{
    /// <summary>
    /// A line of <paramref name="text"/> from the player in <paramref name="slot"/> (0 for a party line), arrived at the
    /// game time <paramref name="time"/> and the time of day <paramref name="stamp"/>.
    /// </summary>
    public ChatLine(int slot, string text, float time, string stamp)
    {
        Slot = slot;
        Text = text ?? string.Empty;
        Time = time;
        Stamp = stamp ?? string.Empty;
    }

    /// <summary>
    /// The sender's slot, from 1 to <see cref="PlayerSlots.Count"/>; 0 for a line about the party.
    /// </summary>
    public int Slot { get; }

    /// <summary>
    /// Whether the line is about the party rather than from a player.
    /// </summary>
    public bool IsParty => !PlayerSlots.IsSlot(Slot);

    public string Text { get; }
    public float Time { get; }
    public string Stamp { get; }
}
