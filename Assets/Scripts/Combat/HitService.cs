using UnityEngine;

/// <summary>
/// Applies a <see cref="Hit"/>: its damage through <see cref="DamageService"/>, then its statuses through
/// <see cref="StatusEffectService"/> if the hit reached a target that survived it. What swings, mob attacks and
/// abilities call. Keeps the two services independent of each other.
/// </summary>
public sealed class HitService
{
    private readonly DamageService damageService;
    private readonly StatusEffectService statusService;

    public HitService(DamageService damageService, StatusEffectService statusService)
    {
        this.damageService = damageService;
        this.statusService = statusService;
    }

    /// <summary>
    /// Applies <paramref name="hit"/> to <paramref name="target"/>. A hit without damage only applies its statuses.
    /// Returns the damage result, which is <c>default</c> when the damage was rejected or there was none.
    /// </summary>
    public DamageResult ApplyHit(DamageReceiver target, in Hit hit, GameObject source = null)
    {
        if (target == null)
        {
            return default;
        }

        DamageResult result = default;
        if (hit.Amount > 0)
        {
            result = damageService.ApplyDamage(target, hit.Amount, hit.Type, source);
            if (!result.Resolved)
            {
                return result;
            }
        }

        if (target.Health == null || target.Health.IsDead)
        {
            return result;
        }

        for (int i = 0; i < hit.StatusCount; i++)
        {
            statusService.Apply(target, hit.GetStatus(i), source);
        }

        return result;
    }
}
