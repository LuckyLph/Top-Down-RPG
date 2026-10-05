using UnityEngine;

/// <summary>
/// One periodic tick that came due: its status, the amount (already scaled by stacks), the source and the
/// source's damage-dealt multiplier captured when the status landed.
/// </summary>
public readonly struct StatusTick
{
    /// <summary>
    /// Creates a due tick of <paramref name="amount"/> for <paramref name="definition"/> from
    /// <paramref name="source"/>, scaled later by <paramref name="dealtMultiplier"/> when it deals damage.
    /// </summary>
    public StatusTick(StatusEffectDefinition definition, int amount, GameObject source, float dealtMultiplier)
    {
        Definition = definition;
        Amount = amount;
        Source = source;
        DealtMultiplier = dealtMultiplier;
    }

    public StatusEffectDefinition Definition { get; }
    public int Amount { get; }
    public GameObject Source { get; }
    public float DealtMultiplier { get; }
}
