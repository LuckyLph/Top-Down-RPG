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
        DeathPhysics.Disable(gameObject);
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

    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
