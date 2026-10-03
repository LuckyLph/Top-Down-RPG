using System;
using UnityEngine;

/// <summary>
/// The one place ability effects are applied, mirroring <see cref="DamageService"/>: it acts only where
/// <see cref="IGameAuthority.IsAuthoritative"/>, once per cast, when the cast starts. A <see cref="AbilityTargeting.Unit"/>
/// ability with an effect heals its target through <see cref="DamageService"/> and applies its <see cref="Hit"/>
/// through <see cref="HitService"/>, with the caster as the source so the faction rules apply. Other targeting has
/// no effect yet.
/// </summary>
public sealed class AbilityService
{
    private readonly IGameAuthority authority;
    private readonly DamageService damageService;
    private readonly HitService hitService;

    public AbilityService(IGameAuthority authority, DamageService damageService, HitService hitService)
    {
        this.authority = authority;
        this.damageService = damageService;
        this.hitService = hitService;
    }

    public event Action<PlayerAbilities, AbilityCast> CastApplied;

    /// <summary>
    /// Applies <paramref name="cast"/> by <paramref name="caster"/> on the authoritative machine; does nothing
    /// elsewhere. Returns whether it applied.
    /// </summary>
    public bool Apply(PlayerAbilities caster, in AbilityCast cast)
    {
        if (!authority.IsAuthoritative || caster == null || cast.Ability == null)
        {
            return false;
        }

        ApplyEffect(caster.gameObject, cast);
        CastApplied?.Invoke(caster, cast);
        return true;
    }

    private void ApplyEffect(GameObject source, in AbilityCast cast)
    {
        AbilityDefinition ability = cast.Ability;
        if (ability.Targeting != AbilityTargeting.Unit || !ability.HasEffect)
        {
            return;
        }

        Health targetHealth = cast.Aim.Target.Health;
        DamageReceiver target = targetHealth != null ? targetHealth.GetComponent<DamageReceiver>() : null;
        if (target == null)
        {
            return;
        }

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
