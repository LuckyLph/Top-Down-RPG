using System;

/// <summary>
/// The one place ability effects will be applied, mirroring <see cref="DamageService"/>: it acts only where
/// <see cref="IGameAuthority.IsAuthoritative"/>. Abilities are blanks for now, so it only announces each cast.
/// </summary>
public sealed class AbilityService
{
    private readonly IGameAuthority authority;

    public AbilityService(IGameAuthority authority)
    {
        this.authority = authority;
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

        CastApplied?.Invoke(caster, cast);
        return true;
    }
}
