/// <summary>
/// The player's ability slots: four class abilities (Q W E R) followed by two weapon abilities (A S).
/// </summary>
public static class AbilitySlots
{
    public const int Count = 6;

    private static readonly string[] KeyNames = { "Q", "W", "E", "R", "A", "S" };

    /// <summary>
    /// The key bound to <paramref name="slot"/>, or an empty string outside the bar.
    /// </summary>
    public static string KeyName(int slot)
    {
        return slot >= 0 && slot < KeyNames.Length ? KeyNames[slot] : string.Empty;
    }
}
