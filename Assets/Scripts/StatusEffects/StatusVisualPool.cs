using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Gameplay-scope pool of <see cref="StatusVisual"/> instances, one free list per prefab. New instances are moved
/// into the Gameplay scene so they never outlive the session; every instance is destroyed on dispose.
/// </summary>
public sealed class StatusVisualPool : IDisposable
{
    private readonly Scene scene;
    private readonly Dictionary<StatusVisual, Stack<StatusVisual>> free = new();
    private readonly List<StatusVisual> created = new();

    public StatusVisualPool(Scene scene)
    {
        this.scene = scene;
    }

    internal int CreatedCount => created.Count;

    public StatusVisual Get(StatusVisual prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        if (free.TryGetValue(prefab, out Stack<StatusVisual> pooled))
        {
            while (pooled.Count > 0)
            {
                StatusVisual reused = pooled.Pop();
                if (reused != null)
                {
                    return reused;
                }
            }
        }

        StatusVisual instance = Object.Instantiate(prefab);
        instance.SourcePrefab = prefab;
        instance.Hide();
        if (scene.IsValid() && scene.isLoaded)
        {
            SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
        }

        created.Add(instance);
        return instance;
    }

    public void Release(StatusVisual visual)
    {
        if (visual == null)
        {
            return;
        }

        visual.Hide();
        if (!free.TryGetValue(visual.SourcePrefab, out Stack<StatusVisual> pooled))
        {
            pooled = new Stack<StatusVisual>();
            free[visual.SourcePrefab] = pooled;
        }

        pooled.Push(visual);
    }

    public void Dispose()
    {
        foreach (StatusVisual visual in created)
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
