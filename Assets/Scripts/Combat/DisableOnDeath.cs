using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DisableOnDeath : MonoBehaviour
{
    private readonly List<MonoBehaviour> disabledBehaviours = new();
    private readonly List<Collider2D> disabledColliders = new();

    private Health health;
    private Rigidbody2D body;
    private bool bodyWasSimulated;
    private bool handledDeath;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        health.Died += HandleDied;
        health.Restored += HandleRestored;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
            health.Restored -= HandleRestored;
        }
    }

    private void HandleDied(Health _)
    {
        if (handledDeath)
        {
            return;
        }

        handledDeath = true;

        DisableGameplayBehaviours();
        RecordEnabledColliders();
        bodyWasSimulated = body != null && body.simulated;
        DeathPhysics.Disable(gameObject);
    }

    private void HandleRestored(Health _)
    {
        if (!handledDeath)
        {
            return;
        }

        handledDeath = false;

        foreach (MonoBehaviour behaviour in disabledBehaviours)
        {
            if (behaviour != null)
            {
                behaviour.enabled = true;
            }
        }

        foreach (Collider2D disabledCollider in disabledColliders)
        {
            if (disabledCollider != null)
            {
                disabledCollider.enabled = true;
            }
        }

        if (body != null)
        {
            body.simulated = bodyWasSimulated;
        }

        disabledBehaviours.Clear();
        disabledColliders.Clear();
    }

    private void DisableGameplayBehaviours()
    {
        disabledBehaviours.Clear();
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null ||
                behaviour == this ||
                behaviour is Health ||
                behaviour is DamageReceiver ||
                behaviour is NetworkObject ||
                behaviour is NetworkBehaviour ||
                !behaviour.enabled)
            {
                continue;
            }

            behaviour.enabled = false;
            disabledBehaviours.Add(behaviour);
        }
    }

    private void RecordEnabledColliders()
    {
        disabledColliders.Clear();
        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].enabled)
            {
                disabledColliders.Add(colliders[i]);
            }
        }
    }

    private void ResolveReferences()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }

        if (body == null)
        {
            TryGetComponent(out body);
        }
    }
}
