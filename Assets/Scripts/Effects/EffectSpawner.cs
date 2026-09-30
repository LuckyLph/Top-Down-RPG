using System;
using UnityEngine;

public sealed class EffectSpawner
{
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
