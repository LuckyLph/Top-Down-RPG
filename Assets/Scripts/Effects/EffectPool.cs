using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Gameplay-scope pool of one-shot <see cref="EffectPlayer"/>s (hit impacts, mob deaths), one free list per prefab, as
/// <see cref="ProjectileVisualPool"/> is for projectiles. New instances are moved into the Gameplay scene so they never
/// outlive the session; every instance is destroyed on dispose. Playing an effect allocates nothing once warm.
/// </summary>
public sealed class EffectPool : IDisposable
{
    private readonly Scene scene;
    private readonly Dictionary<EffectPlayer, Stack<EffectPlayer>> free = new();
    private readonly List<EffectPlayer> created = new();

    /// <summary>
    /// Creates an empty pool that moves new instances into <paramref name="scene"/> when it is loaded.
    /// </summary>
    public EffectPool(Scene scene)
    {
        this.scene = scene;
    }

    /// <summary>
    /// Raised each time an effect starts, with the instance playing it; for tests.
    /// </summary>
    internal event Action<EffectPlayer> Played;

    internal int CreatedCount => created.Count;

    /// <summary>
    /// Plays <paramref name="prefab"/>'s <paramref name="variant"/> facing <paramref name="direction"/> at
    /// <paramref name="position"/>, sorted as if standing at height <paramref name="sortingY"/>, on a released instance
    /// when one is free (skipping destroyed ones) or a new one. Returns the instance; null for a null prefab.
    /// </summary>
    public EffectPlayer Play(EffectPlayer prefab, int variant, int direction, Vector2 position, float sortingY)
    {
        if (prefab == null)
        {
            return null;
        }

        EffectPlayer instance = Take(prefab);
        instance.Play(variant, direction, position, sortingY, this);
        Played?.Invoke(instance);
        return instance;
    }

    /// <summary>
    /// Hides <paramref name="effect"/> and returns it to its prefab's free list; null is ignored.
    /// </summary>
    public void Release(EffectPlayer effect)
    {
        if (effect == null)
        {
            return;
        }

        effect.Hide();
        if (!free.TryGetValue(effect.SourcePrefab, out Stack<EffectPlayer> pooled))
        {
            pooled = new Stack<EffectPlayer>();
            free[effect.SourcePrefab] = pooled;
        }

        pooled.Push(effect);
    }

    /// <summary>
    /// Destroys every instance the pool created, playing or free, and forgets them.
    /// </summary>
    public void Dispose()
    {
        foreach (EffectPlayer effect in created)
        {
            if (effect != null)
            {
                Object.Destroy(effect.gameObject);
            }
        }

        created.Clear();
        free.Clear();
    }

    /// <summary>
    /// A hidden instance of <paramref name="prefab"/>: a released one when available, otherwise a new one moved into
    /// the pool's scene.
    /// </summary>
    private EffectPlayer Take(EffectPlayer prefab)
    {
        if (free.TryGetValue(prefab, out Stack<EffectPlayer> pooled))
        {
            while (pooled.Count > 0)
            {
                EffectPlayer reused = pooled.Pop();
                if (reused != null)
                {
                    return reused;
                }
            }
        }

        EffectPlayer instance = Object.Instantiate(prefab);
        instance.SourcePrefab = prefab;
        instance.Hide();
        if (scene.IsValid() && scene.isLoaded)
        {
            SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
        }

        created.Add(instance);
        return instance;
    }
}
