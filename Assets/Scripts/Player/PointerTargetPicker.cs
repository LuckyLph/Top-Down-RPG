using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds the living unit under a world point: a non-allocating overlap within the pick radius on the enemy and
/// ally layers. Enemies need a <see cref="DamageReceiver"/>; allies must be registered players. There is no
/// line-of-sight test. The collider-to-receiver lookup is cached (<see cref="EnemyHealthLookup"/>), so picking every
/// frame does no component lookups once warm.
/// </summary>
public sealed class PointerTargetPicker
{
    private const int MaxHits = 16;

    private readonly Collider2D[] hits = new Collider2D[MaxHits];
    private readonly EnemyHealthLookup enemies = new();
    private readonly PlayerControlSettings settings;
    private readonly IPlayerRegistry players;

    /// <summary>
    /// Picks with the radius and layers in <paramref name="settings"/> (null picks nothing), recognising allies
    /// through <paramref name="players"/>.
    /// </summary>
    public PointerTargetPicker(PlayerControlSettings settings, IPlayerRegistry players)
    {
        this.settings = settings;
        this.players = players;
    }

    /// <summary>
    /// The living unit whose collider centre is closest to <paramref name="worldPoint"/>, or an empty target.
    /// </summary>
    public UnitTarget Pick(Vector2 worldPoint)
    {
        if (settings == null)
        {
            return default;
        }

        ContactFilter2D filter = new() { useTriggers = false };
        filter.SetLayerMask(settings.EnemyLayers | settings.AllyLayers);
        int count = Physics2D.OverlapCircle(worldPoint, settings.PickRadius, filter, hits);

        UnitTarget best = default;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = hits[i];
            UnitTarget candidate = Classify(hit);
            if (!candidate.Exists)
            {
                continue;
            }

            float distance = ((Vector2)hit.bounds.center - worldPoint).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        for (int i = 0; i < count; i++)
        {
            hits[i] = null;
        }

        return best;
    }

    /// <summary>
    /// The unit behind <paramref name="hit"/>: a living enemy on an enemy layer, else a living registered player whose
    /// hierarchy holds the collider on an ally layer, else an empty target.
    /// </summary>
    private UnitTarget Classify(Collider2D hit)
    {
        int layerBit = 1 << hit.gameObject.layer;
        if ((settings.EnemyLayers.value & layerBit) != 0)
        {
            Health enemy = enemies.Find(hit);
            if (enemy != null && !enemy.IsDead)
            {
                return new UnitTarget(enemy, hit, UnitTeam.Enemy);
            }
        }

        if ((settings.AllyLayers.value & layerBit) != 0 && players != null)
        {
            IReadOnlyList<PlayerHandle> party = players.Players;
            for (int i = 0; i < party.Count; i++)
            {
                PlayerHandle handle = party[i];
                if (handle.IsAlive && handle.Transform != null && hit.transform.IsChildOf(handle.Transform))
                {
                    return new UnitTarget(handle.Health, hit, UnitTeam.Ally);
                }
            }
        }

        return default;
    }
}
