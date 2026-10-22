using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Gameplay-scope pool of <see cref="ProjectileVisual"/> instances, one free list per prefab, as
/// <see cref="StatusVisualPool"/> is for status visuals. New instances are moved into the Gameplay scene so they never
/// outlive the session; every instance is destroyed on dispose.
/// </summary>
public sealed class ProjectileVisualPool : IDisposable
{
    private readonly Scene scene;
    private readonly Dictionary<ProjectileVisual, Stack<ProjectileVisual>> free = new();
    private readonly List<ProjectileVisual> created = new();

    /// <summary>
    /// Creates an empty pool that moves new instances into <paramref name="scene"/> when it is loaded.
    /// </summary>
    public ProjectileVisualPool(Scene scene)
    {
        this.scene = scene;
    }

    internal int CreatedCount => created.Count;

    /// <summary>
    /// A hidden instance of <paramref name="prefab"/>: a released one when available (skipping destroyed ones),
    /// otherwise a new one. Null for a null prefab.
    /// </summary>
    public ProjectileVisual Get(ProjectileVisual prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        if (free.TryGetValue(prefab, out Stack<ProjectileVisual> pooled))
        {
            while (pooled.Count > 0)
            {
                ProjectileVisual reused = pooled.Pop();
                if (reused != null)
                {
                    return reused;
                }
            }
        }

        ProjectileVisual instance = Object.Instantiate(prefab);
        instance.SourcePrefab = prefab;
        instance.Hide();
        if (scene.IsValid() && scene.isLoaded)
        {
            SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
        }

        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// Hides <paramref name="visual"/> and returns it to its prefab's free list; null is ignored.
    /// </summary>
    public void Release(ProjectileVisual visual)
    {
        if (visual == null)
        {
            return;
        }

        visual.Hide();
        if (!free.TryGetValue(visual.SourcePrefab, out Stack<ProjectileVisual> pooled))
        {
            pooled = new Stack<ProjectileVisual>();
            free[visual.SourcePrefab] = pooled;
        }

        pooled.Push(visual);
    }

    /// <summary>
    /// Destroys every instance the pool created, in use or free, and forgets them.
    /// </summary>
    public void Dispose()
    {
        foreach (ProjectileVisual visual in created)
        {
            if (visual != null)
            {
                Object.Destroy(visual.gameObject);
            }
        }

        created.Clear();
        free.Clear();
    }
}
