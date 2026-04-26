using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DisableOnDeath : MonoBehaviour
{
    private Health health;
    private bool handledDeath;

    private void Awake()
    {
        ResolveHealth();
    }

    private void OnEnable()
    {
        ResolveHealth();
        health.Died += HandleDied;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
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
        DisableColliders();
        DisableRigidbody();
    }

    private void DisableGameplayBehaviours()
    {
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null ||
                behaviour == this ||
                behaviour is Health ||
                behaviour is DamageReceiver)
            {
                continue;
            }

            behaviour.enabled = false;
        }
    }

    private void DisableColliders()
    {
        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }
    }

    private void DisableRigidbody()
    {
        if (!TryGetComponent(out Rigidbody2D rb))
        {
            return;
        }

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = false;
    }

    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
