using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds the <see cref="Health"/> behind an enemy collider's <see cref="DamageReceiver"/>, caching each answer per
/// collider so lookups every frame do no component searches once warm.
/// </summary>
public sealed class EnemyHealthLookup
{
    private const int MaxCachedColliders = 64;

    private readonly Dictionary<Collider2D, Health> healthByCollider = new(MaxCachedColliders);

    /// <summary>
    /// The health of the <see cref="DamageReceiver"/> behind <paramref name="collider"/>, or null, cached per collider.
    /// A destroyed cached health is looked up again, and the cache is cleared whenever it fills up.
    /// </summary>
    public Health Find(Collider2D collider)
    {
        if (healthByCollider.TryGetValue(collider, out Health cached) && (ReferenceEquals(cached, null) || cached != null))
        {
            return cached;
        }

        if (healthByCollider.Count >= MaxCachedColliders)
        {
            healthByCollider.Clear();
        }

        DamageReceiver receiver = DamageReceiver.FindFor(collider.transform);
        Health health = receiver != null && receiver.Health != null ? receiver.Health : null;
        healthByCollider[collider] = health;
        return health;
    }
}
