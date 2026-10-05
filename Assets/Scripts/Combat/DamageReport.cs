using UnityEngine;

/// <summary>
/// A hit that landed on a unit, as published on <see cref="CombatEvents.DamageApplied"/>: the HP actually removed
/// (0 for an immune hit), its type and flags, the source (null when replicated from the host) and where its popup
/// appears.
/// </summary>
public readonly struct DamageReport
{
    /// <summary>
    /// Creates a report of <paramref name="amount"/> HP of <paramref name="type"/> damage dealt to
    /// <paramref name="target"/>.
    /// </summary>
    public DamageReport(Health target, int amount, DamageType type, DamageFlags flags, GameObject source, Vector3 popupWorldPosition)
    {
        Target = target;
        Amount = amount;
        Type = type;
        Flags = flags;
        Source = source;
        PopupWorldPosition = popupWorldPosition;
    }

    public Health Target { get; }
    public int Amount { get; }
    public DamageType Type { get; }
    public DamageFlags Flags { get; }
    public GameObject Source { get; }
    public Vector3 PopupWorldPosition { get; }
    public bool IsImmune => (Flags & DamageFlags.Immune) != 0;
}
