using UnityEngine;

/// <summary>
/// A hit that landed on a unit, as published on <see cref="CombatEvents.DamageApplied"/>: the HP actually removed
/// (0 for an immune hit), its type and flags, the source (on a client, its copy of the source unit the host named, or
/// null for an immune hit or a source it does not know), where its popup appears and the point it came from as the host
/// resolved it (Docs/KnownGaps.md, Phase 4), the same on every machine.
/// </summary>
public readonly struct DamageReport
{
    /// <summary>
    /// Creates a report of <paramref name="amount"/> HP of <paramref name="type"/> damage dealt to
    /// <paramref name="target"/>.
    /// </summary>
    public DamageReport(Health target, int amount, DamageType type, DamageFlags flags, GameObject source, Vector3 popupWorldPosition, Vector2? origin = null)
    {
        Target = target;
        Amount = amount;
        Type = type;
        Flags = flags;
        Source = source;
        PopupWorldPosition = popupWorldPosition;
        Origin = origin;
    }

    public Health Target { get; }
    public int Amount { get; }
    public DamageType Type { get; }
    public DamageFlags Flags { get; }
    public GameObject Source { get; }
    public Vector3 PopupWorldPosition { get; }

    /// <summary>
    /// Where the hit came from as the host resolved it (where an arrow was shot from, where an attacker or caster
    /// stood), or null for a hit from no point (a tick, an immune hit, a hit applied directly).
    /// </summary>
    public Vector2? Origin { get; }

    public bool IsImmune => (Flags & DamageFlags.Immune) != 0;
}
