using System;
using UnityEngine;

/// <summary>
/// Spawns one-shot effects from templates, without pooling. Registered as a singleton in the Gameplay scope.
/// </summary>
public sealed class EffectSpawner
{
    /// <summary>
    /// Instantiates <paramref name="template"/> at the scene root, deactivates the copy, runs
    /// <paramref name="configure"/> on it, then activates it and returns it. Throws when the template is null.
    /// </summary>
    public T Spawn<T>(T template, Action<T> configure = null) where T : Component
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        T instance = UnityEngine.Object.Instantiate(template);
        instance.transform.SetParent(null, false);
        instance.gameObject.SetActive(false);
        configure?.Invoke(instance);
        instance.gameObject.SetActive(true);
        return instance;
    }
}
