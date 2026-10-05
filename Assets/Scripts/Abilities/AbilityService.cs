using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one place ability effects are applied, mirroring <see cref="DamageService"/>: it acts only where
/// <see cref="IGameAuthority.IsAuthoritative"/>. <see cref="Apply"/> runs once per cast when it starts and only
/// announces it; <see cref="Resolve"/> runs once per cast that completes (an interrupted cast never resolves) and
/// applies its effect to the units its targeting selects: the target (<see cref="AbilityTargeting.Unit"/>), the
/// caster or the units around it (<see cref="AbilityTargeting.None"/>), the units around the aimed point
/// (<see cref="AbilityTargeting.Point"/>), or the units on a line from the caster (<see cref="AbilityTargeting.Direction"/>).
/// Each unit is healed through <see cref="DamageService"/> and hit through <see cref="HitService"/> with the caster
/// as the source, so the faction rules decide who is affected.
/// </summary>
public sealed class AbilityService
{
    private const float PointRadius = 0.05f;

    private readonly IGameAuthority authority;
    private readonly DamageService damageService;
    private readonly HitService hitService;
    private readonly ContactFilter2D unitFilter;
    private readonly List<Collider2D> overlaps = new();
    private readonly List<DamageReceiver> affected = new();

    /// <summary>
    /// Captures the services effects go through and builds the overlap filter from the unit layers in
    /// <paramref name="settings"/> (triggers ignored).
    /// </summary>
    public AbilityService(IGameAuthority authority, DamageService damageService, HitService hitService, CombatSettings settings)
    {
        this.authority = authority;
        this.damageService = damageService;
        this.hitService = hitService;
        unitFilter = new ContactFilter2D { useLayerMask = true, layerMask = settings.UnitLayers, useTriggers = false };
    }

    public event Action<PlayerAbilities, AbilityCast> CastApplied;
    public event Action<PlayerAbilities, AbilityCast> CastResolved;

    /// <summary>
    /// Announces that <paramref name="caster"/> started <paramref name="cast"/> on the authoritative machine; does
    /// nothing elsewhere. Returns whether it applied.
    /// </summary>
    public bool Apply(PlayerAbilities caster, in AbilityCast cast)
    {
        if (!authority.IsAuthoritative || caster == null || cast.Ability == null)
        {
            return false;
        }

        CastApplied?.Invoke(caster, cast);
        return true;
    }

    /// <summary>
    /// Applies the effect of <paramref name="cast"/>, which <paramref name="caster"/> completed at
    /// <paramref name="casterPosition"/> (where areas around the caster and lines from it start), on the
    /// authoritative machine; does nothing elsewhere. Returns whether it resolved.
    /// </summary>
    public bool Resolve(PlayerAbilities caster, in AbilityCast cast, Vector2 casterPosition)
    {
        if (!authority.IsAuthoritative || caster == null || cast.Ability == null)
        {
            return false;
        }

        if (cast.Ability.HasEffect)
        {
            CollectAffected(caster, cast, casterPosition);
            for (int i = 0; i < affected.Count; i++)
            {
                ApplyTo(affected[i], cast.Ability, caster.gameObject);
            }

            affected.Clear();
        }

        CastResolved?.Invoke(caster, cast);
        return true;
    }

    /// <summary>
    /// Fills the affected list with the units the cast's targeting selects, starting areas and lines at
    /// <paramref name="origin"/>. A Direction cast with no direction or no range selects nothing.
    /// </summary>
    private void CollectAffected(PlayerAbilities caster, in AbilityCast cast, Vector2 origin)
    {
        affected.Clear();
        AbilityDefinition ability = cast.Ability;
        switch (ability.Targeting)
        {
            case AbilityTargeting.Unit:
                Health target = cast.Aim.Target.Health;
                AddUnit(target != null ? target.GetComponent<DamageReceiver>() : null);
                break;
            case AbilityTargeting.None:
                if (ability.AreaRadius > 0f)
                {
                    AddOverlaps(Physics2D.OverlapCircle(origin, ability.AreaRadius, unitFilter, overlaps));
                }
                else
                {
                    AddUnit(caster.GetComponent<DamageReceiver>());
                }

                break;
            case AbilityTargeting.Point:
                AddOverlaps(Physics2D.OverlapCircle(cast.Aim.Point, Mathf.Max(PointRadius, ability.AreaRadius), unitFilter, overlaps));
                break;
            case AbilityTargeting.Direction:
                Vector2 direction = cast.Aim.Direction;
                if (direction.sqrMagnitude < 0.0001f || ability.Range <= 0f)
                {
                    break;
                }

                direction.Normalize();
                float width = Mathf.Max(PointRadius, ability.AreaRadius) * 2f;
                Vector2 center = origin + direction * (ability.Range * 0.5f);
                Vector2 size = new(ability.Range + width, width);
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                AddOverlaps(Physics2D.OverlapCapsule(center, size, CapsuleDirection2D.Horizontal, angle, unitFilter, overlaps));
                break;
        }
    }

    /// <summary>
    /// Adds the <see cref="DamageReceiver"/> behind each of the first <paramref name="count"/> overlap results, then
    /// clears the overlap buffer.
    /// </summary>
    private void AddOverlaps(int count)
    {
        for (int i = 0; i < count; i++)
        {
            AddUnit(DamageReceiver.FindFor(overlaps[i].transform));
        }

        overlaps.Clear();
    }

    /// <summary>
    /// Adds <paramref name="unit"/> to the affected list once, ignoring null and units already in it.
    /// </summary>
    private void AddUnit(DamageReceiver unit)
    {
        if (unit != null && !affected.Contains(unit))
        {
            affected.Add(unit);
        }
    }

    /// <summary>
    /// Heals <paramref name="target"/> when the ability has a heal, then applies its hit when it carries damage or
    /// statuses, both with <paramref name="source"/> as the source.
    /// </summary>
    private void ApplyTo(DamageReceiver target, AbilityDefinition ability, GameObject source)
    {
        if (ability.Heal > 0)
        {
            damageService.ApplyHeal(target, ability.Heal, source);
        }

        Hit hit = ability.Hit;
        if (hit.Amount > 0 || hit.StatusCount > 0)
        {
            hitService.ApplyHit(target, hit, source);
        }
    }
}
