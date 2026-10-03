/// <summary>
/// A unit's defence against one damage type at one moment: resistance before clamping, immunity and the
/// damage-taken multiplier.
/// </summary>
public readonly struct DefenseSnapshot
{
    public DefenseSnapshot(int resistancePercent, bool immune, float damageTakenMultiplier)
    {
        ResistancePercent = resistancePercent;
        Immune = immune;
        DamageTakenMultiplier = damageTakenMultiplier;
    }

    public static DefenseSnapshot None => new(0, false, 1f);

    public int ResistancePercent { get; }
    public bool Immune { get; }
    public float DamageTakenMultiplier { get; }
}
