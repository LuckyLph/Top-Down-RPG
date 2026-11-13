using System.Collections.Generic;

/// <summary>
/// The player slots (Docs/UiRevamp.md, Decisions: Player slots): numbers from 1 to <see cref="Count"/> that the host
/// gives players as they join, the lowest free one each time, and each slot's colour role and label. A slot is told
/// by its colour and, in a party frame, its number.
/// </summary>
public static class PlayerSlots
{
    public const int Count = 4;

    private static readonly string[] Labels = { string.Empty, "Player 1", "Player 2", "Player 3", "Player 4" };

    /// <summary>
    /// Whether <paramref name="number"/> names a slot.
    /// </summary>
    public static bool IsSlot(int number)
    {
        return number >= 1 && number <= Count;
    }

    /// <summary>
    /// The lowest slot no number in <paramref name="taken"/> holds, or 0 when every slot is taken.
    /// </summary>
    public static int LowestFree(IEnumerable<int> taken)
    {
        bool[] used = new bool[Count + 1];
        foreach (int number in taken)
        {
            if (IsSlot(number))
            {
                used[number] = true;
            }
        }

        for (int number = 1; number <= Count; number++)
        {
            if (!used[number])
            {
                return number;
            }
        }

        return 0;
    }

    /// <summary>
    /// The colour role of slot <paramref name="number"/>, <see cref="UiColour.Player1"/> to <see cref="UiColour.Player4"/>.
    /// </summary>
    public static UiColour Role(int number)
    {
        return IsSlot(number) ? UiColour.Player1 + (number - 1) : UiColour.Player1;
    }

    /// <summary>
    /// The label of slot <paramref name="number"/>, "Player 2"; empty for no slot. Built once, so reading it allocates
    /// nothing.
    /// </summary>
    public static string Label(int number)
    {
        return IsSlot(number) ? Labels[number] : string.Empty;
    }
}
