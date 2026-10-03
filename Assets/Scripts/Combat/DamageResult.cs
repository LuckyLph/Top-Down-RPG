/// <summary>
/// A resolved hit: its type, the amount before mitigation, the amount dealt and what happened to it.
/// <c>default</c> means the hit was rejected.
/// </summary>
public readonly struct DamageResult
{
    public DamageResult(DamageType type, int rawAmount, int amount, DamageFlags flags)
    {
        Type = type;
        RawAmount = rawAmount;
        Amount = amount;
        Flags = flags;
    }

    public DamageType Type { get; }
    public int RawAmount { get; }
    public int Amount { get; }
    public DamageFlags Flags { get; }
    public bool IsImmune => (Flags & DamageFlags.Immune) != 0;

    /// <summary>
    /// Whether the hit reached the target: it dealt damage or the target was immune.
    /// </summary>
    public bool Resolved => Amount > 0 || IsImmune;

    public DamageResult WithAmount(int amount)
    {
        return new DamageResult(Type, RawAmount, amount, Flags);
    }
}
