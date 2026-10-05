using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A death reaction for units that come back (the player): on death it disables the unit's other behaviours,
/// colliders and physics, and when <see cref="Health.Restored"/> fires it re-enables exactly what it turned off.
/// <see cref="Health"/>, <see cref="DamageReceiver"/> and network components stay enabled.
/// </summary>
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

    /// <summary>
    /// Caches the sibling health and rigidbody.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Subscribes to death and restore.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        health.Died += HandleDied;
        health.Restored += HandleRestored;
    }

    /// <summary>
    /// Unsubscribes from death and restore.
    /// </summary>
    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
            health.Restored -= HandleRestored;
        }
    }

    /// <summary>
    /// Once per death: disables the gameplay behaviours, records which colliders were enabled and whether the body
    /// was simulated, then switches the unit's physics off.
    /// </summary>
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

    /// <summary>
    /// After a handled death, re-enables the behaviours and colliders it disabled and restores the body's previous
    /// simulation state; ignored unless a death was handled.
    /// </summary>
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

    /// <summary>
    /// Disables and remembers every enabled sibling behaviour except this one, <see cref="Health"/>,
    /// <see cref="DamageReceiver"/> and network components.
    /// </summary>
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

    /// <summary>
    /// Remembers the sibling colliders that are enabled, so a restore re-enables only those.
    /// </summary>
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

    /// <summary>
    /// Looks up the sibling <see cref="Health"/> and optional <see cref="Rigidbody2D"/> when not cached yet.
    /// </summary>
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
