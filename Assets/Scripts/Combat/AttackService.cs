using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves weapon attacks at contact (Docs/Animation.md, Attack timeline), mirroring <see cref="AbilityService"/>: it
/// acts only where <see cref="IGameAuthority.IsAuthoritative"/>. The weapon's hit area, a box turned to the attack's
/// direction ahead of where the attacker stood, is searched once with a non-allocating overlap on the unit layers of
/// <see cref="CombatSettings"/> (triggers ignored), and every unit in it but the attacker is hit once through
/// <see cref="HitService"/> with the attacker as the source, so the faction rules decide who is affected.
/// </summary>
public sealed class AttackService
{
    private readonly IGameAuthority authority;
    private readonly HitService hitService;
    private readonly ContactFilter2D unitFilter;
    private readonly List<Collider2D> overlaps = new();
    private readonly List<DamageReceiver> struck = new();

    /// <summary>
    /// Captures the authority and the hit service hits go through, and builds the overlap filter from the unit layers
    /// in <paramref name="settings"/>.
    /// </summary>
    public AttackService(IGameAuthority authority, HitService hitService, CombatSettings settings)
    {
        this.authority = authority;
        this.hitService = hitService;
        unitFilter = new ContactFilter2D { useLayerMask = true, layerMask = settings.UnitLayers, useTriggers = false };
    }

    /// <summary>
    /// On the authoritative machine, hits every unit in <paramref name="weapon"/>'s hit area for
    /// <paramref name="attacker"/> striking toward <paramref name="direction"/> (down when zero) from
    /// <paramref name="attackerPosition"/>, each once and never the attacker. Returns how many units the hit reached;
    /// 0 elsewhere.
    /// </summary>
    public int Resolve(Transform attacker, PlayerWeapon weapon, Vector2 direction, Vector2 attackerPosition)
    {
        if (!authority.IsAuthoritative || attacker == null || weapon == null)
        {
            return 0;
        }

        Vector2 aim = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        int count = Physics2D.OverlapBox(weapon.HitCentre(attackerPosition, aim), weapon.HitSize, Vector2.SignedAngle(Vector2.up, aim), unitFilter, overlaps);
        for (int i = 0; i < count; i++)
        {
            DamageReceiver receiver = DamageReceiver.FindFor(overlaps[i].transform);
            if (receiver != null && !struck.Contains(receiver) && !IsPartOf(receiver.transform, attacker))
            {
                struck.Add(receiver);
            }
        }

        overlaps.Clear();
        Hit hit = weapon.Hit;
        GameObject source = attacker.gameObject;
        int reached = 0;
        for (int i = 0; i < struck.Count; i++)
        {
            if (hitService.ApplyHit(struck[i], hit, source))
            {
                reached++;
            }
        }

        struck.Clear();
        return reached;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> is <paramref name="attacker"/> or one of its children.
    /// </summary>
    private static bool IsPartOf(Transform unit, Transform attacker)
    {
        return unit == attacker || unit.IsChildOf(attacker);
    }
}
