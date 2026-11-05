using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves melee attacks at contact (Docs/Animation.md, Attack timeline), players' weapons and mobs' attacks alike,
/// mirroring <see cref="AbilityService"/>: it acts only where <see cref="IGameAuthority.IsAuthoritative"/>. The hit
/// area, a box turned to the attack's direction ahead of where the attacker stood, is searched once with a
/// non-allocating overlap on the unit layers of <see cref="CombatSettings"/> (triggers ignored), and every unit in it
/// but the attacker is hit once through <see cref="HitService"/> with the attacker as the source and where it stood as
/// the hit's origin, so the faction rules decide who is affected and every machine turns the hit's feedback from the
/// same point. Off the authority, a client predicts its own player's melee hit at its contact
/// (<see cref="Predict"/>, Docs/KnownGaps.md, Phase 4), so its contact frame holds without waiting a round trip.
/// </summary>
public sealed class AttackService
{
    private readonly IGameAuthority authority;
    private readonly HitService hitService;
    private readonly CombatEvents combatEvents;
    private readonly ContactFilter2D unitFilter;
    private readonly List<Collider2D> overlaps = new();
    private readonly List<DamageReceiver> struck = new();

    /// <summary>
    /// Captures the authority, the hit service hits go through and the events a predicted hit is raised on, and builds
    /// the overlap filter from the unit layers in <paramref name="settings"/>.
    /// </summary>
    public AttackService(IGameAuthority authority, HitService hitService, CombatSettings settings, CombatEvents combatEvents)
    {
        this.authority = authority;
        this.hitService = hitService;
        this.combatEvents = combatEvents;
        unitFilter = new ContactFilter2D { useLayerMask = true, layerMask = settings.UnitLayers, useTriggers = false };
    }

    /// <summary>
    /// On the authoritative machine, hits every unit in <paramref name="weapon"/>'s hit area for
    /// <paramref name="attacker"/> striking toward <paramref name="direction"/> (down when zero) from
    /// <paramref name="attackerPosition"/>, each once and never the attacker, the hit coming from that position.
    /// Returns how many units the hit reached; 0 elsewhere.
    /// </summary>
    public int Resolve(Transform attacker, PlayerWeapon weapon, Vector2 direction, Vector2 attackerPosition)
    {
        if (weapon == null)
        {
            return 0;
        }

        Vector2 aim = Aim(direction);
        return Resolve(attacker, weapon.Hit, weapon.HitCentre(attackerPosition, aim), weapon.HitSize, aim, attackerPosition);
    }

    /// <summary>
    /// On the authoritative machine, hits every unit in a box of <paramref name="size"/> (its width across the attack
    /// and its depth along it) centred on <paramref name="centre"/> and turned to <paramref name="direction"/> (down
    /// when zero) with <paramref name="hit"/>, each once and never <paramref name="attacker"/>, which is the source,
    /// the hit coming from <paramref name="origin"/>, where the attacker stood. Returns how many units the hit reached;
    /// 0 elsewhere. A mob's attack resolves here at its contact.
    /// </summary>
    public int Resolve(Transform attacker, in Hit hit, Vector2 centre, Vector2 size, Vector2 direction, Vector2 origin)
    {
        if (!authority.IsAuthoritative || attacker == null)
        {
            return 0;
        }

        Vector2 aim = Aim(direction);
        int count = Physics2D.OverlapBox(centre, size, Vector2.SignedAngle(Vector2.up, aim), unitFilter, overlaps);
        for (int i = 0; i < count; i++)
        {
            DamageReceiver receiver = DamageReceiver.FindFor(overlaps[i].transform);
            if (receiver != null && !struck.Contains(receiver) && !IsPartOf(receiver.transform, attacker))
            {
                struck.Add(receiver);
            }
        }

        overlaps.Clear();
        GameObject source = attacker.gameObject;
        int reached = 0;
        for (int i = 0; i < struck.Count; i++)
        {
            if (hitService.ApplyHit(struck[i], hit, source, origin))
            {
                reached++;
            }
        }

        struck.Clear();
        return reached;
    }

    /// <summary>
    /// Off the authority (a client, whose own hits the host resolves a round trip later): whether
    /// <paramref name="weapon"/>'s melee hit for <paramref name="attacker"/> striking toward
    /// <paramref name="direction"/> from <paramref name="attackerPosition"/> would land as this machine shows the
    /// units, on a living unit in its hit area that is not the attacker, not of its faction and not immune to the
    /// weapon's damage type. When it would, raises <see cref="CombatEvents.HitPredicted"/> with the attacker, so its
    /// contact frame holds now as the host's own does; the host's hit, when it arrives, shows on the struck unit as
    /// before. False, raising nothing, on the authority, whose hit lands at contact, and for a weapon that fires a
    /// projectile.
    /// </summary>
    public bool Predict(Transform attacker, PlayerWeapon weapon, Vector2 direction, Vector2 attackerPosition)
    {
        if (authority.IsAuthoritative || attacker == null || weapon == null || weapon.FiresProjectile)
        {
            return false;
        }

        Vector2 aim = Aim(direction);
        int count = Physics2D.OverlapBox(weapon.HitCentre(attackerPosition, aim), weapon.HitSize, Vector2.SignedAngle(Vector2.up, aim), unitFilter, overlaps);
        DamageReceiver attackerUnit = CombatFactions.UnitOf(attacker.gameObject);
        DamageType type = weapon.Hit.Type;
        bool lands = false;
        for (int i = 0; i < count && !lands; i++)
        {
            DamageReceiver receiver = DamageReceiver.FindFor(overlaps[i].transform);
            lands = receiver != null && !IsPartOf(receiver.transform, attacker) && receiver.Health != null && !receiver.Health.IsDead
                    && !CombatFactions.SameFaction(attackerUnit, receiver) && !receiver.GetDefense(type).Immune;
        }

        overlaps.Clear();
        if (lands)
        {
            combatEvents.PublishPredicted(attacker.gameObject);
        }

        return lands;
    }

    /// <summary>
    /// <paramref name="direction"/> normalised, or down when it is zero.
    /// </summary>
    private static Vector2 Aim(Vector2 direction)
    {
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> is <paramref name="attacker"/> or one of its children.
    /// </summary>
    private static bool IsPartOf(Transform unit, Transform attacker)
    {
        return unit == attacker || unit.IsChildOf(attacker);
    }
}
