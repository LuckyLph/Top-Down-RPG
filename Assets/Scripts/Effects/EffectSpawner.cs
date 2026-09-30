using System;
using UnityEngine;

// Spawns transient effects (death animations, hit sparks) into the active scene, which is the
// current area, so they are cleaned up with it. Future home for pooling and semantic VFX cues.
public sealed class EffectSpawner
{
    // The instance is configured while inactive, so its Awake/OnEnable see the final setup.
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
