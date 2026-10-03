using UnityEngine;

/// <summary>
/// One periodic tick that came due: its status, the amount (already scaled by stacks), the source and the
/// source's damage-dealt multiplier captured when the status landed.
/// </summary>
public readonly struct StatusTick
{
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
