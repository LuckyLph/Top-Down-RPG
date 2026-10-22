using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The physics behind <see cref="IProjectileQueries"/>: a non-allocating circle cast against the wall and unit layers
/// (triggers ignored). A collider on a wall layer is a wall; any other is the unit of its
/// <see cref="DamageReceiver"/>, looked up once per collider and cached, and a collider without one is left out.
/// </summary>
public sealed class PhysicsProjectileQueries : IProjectileQueries
{
    private const int MaxCachedColliders = 128;

    private readonly ContactFilter2D filter;
    private readonly int wallLayers;
    private readonly List<RaycastHit2D> hits = new();
    private readonly Dictionary<Collider2D, DamageReceiver> receivers = new(MaxCachedColliders);

    /// <summary>
    /// Casts against <paramref name="unitLayers"/> and <paramref name="wallLayers"/>.
    /// </summary>
    public PhysicsProjectileQueries(LayerMask unitLayers, LayerMask wallLayers)
    {
        this.wallLayers = wallLayers;
        filter = new ContactFilter2D { useLayerMask = true, layerMask = unitLayers | wallLayers, useTriggers = false };
    }

    /// <summary>
    /// Fills <paramref name="results"/> with the walls and units the circle meets along the step.
    /// </summary>
    public void Cast(Vector2 origin, Vector2 direction, float distance, float radius, List<ProjectileContact> results)
    {
        results.Clear();
        int count = Physics2D.CircleCast(origin, radius, direction, filter, hits, distance);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = hits[i];
            Collider2D collider = hit.collider;
            if ((wallLayers & (1 << collider.gameObject.layer)) != 0)
            {
                results.Add(new ProjectileContact(hit.distance, null));
                continue;
            }

            DamageReceiver unit = ReceiverOf(collider);
            if (unit != null)
            {
                results.Add(new ProjectileContact(hit.distance, unit));
            }
        }

        hits.Clear();
    }

    /// <summary>
    /// The receiver behind <paramref name="collider"/>, or null, cached per collider; a destroyed cached receiver is
    /// looked up again and the cache is cleared whenever it fills up.
    /// </summary>
    private DamageReceiver ReceiverOf(Collider2D collider)
    {
        if (receivers.TryGetValue(collider, out DamageReceiver cached) && (ReferenceEquals(cached, null) || cached != null))
        {
            return cached;
        }

        if (receivers.Count >= MaxCachedColliders)
        {
            receivers.Clear();
        }

        DamageReceiver receiver = DamageReceiver.FindFor(collider.transform);
        receivers[collider] = receiver;
        return receiver;
    }
}
