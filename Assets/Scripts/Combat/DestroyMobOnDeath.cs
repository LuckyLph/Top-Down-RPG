using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// A mob's death reaction: on the first death it turns off the mob's physics and plays its death effect where it
/// stood, from the pooled effects, then the authority despawns the network object (or destroys a non-networked mob)
/// while other machines hide the body until the despawn reaches them.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DestroyMobOnDeath : MonoBehaviour
{
    [SerializeField, Tooltip("The death effect played where the mob stood, its feet on the mob's position.")]
    private EffectPlayer deathEffect;

    private Health health;
    private EffectPool effects;
    private IGameAuthority authority;
    private bool handledDeath;

    public EffectPlayer DeathEffect => deathEffect;

    /// <summary>
    /// Finds the health.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Injects the pool the death effect plays from and the authority that decides who removes the mob.
    /// </summary>
    [Inject]
    public void Construct(EffectPool effectPool, IGameAuthority gameAuthority)
    {
        effects = effectPool;
        authority = gameAuthority;
    }

    /// <summary>
    /// Subscribes to the mob's death.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();

        if (health != null)
        {
            health.Died += HandleDied;
        }
    }

    /// <summary>
    /// Unsubscribes from the mob's death.
    /// </summary>
    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
        }
    }

    /// <summary>
    /// Runs once per mob: disables its physics and plays the death effect; then a non-authoritative machine hides the
    /// body, while the authority despawns the spawned network object or destroys the game object.
    /// </summary>
    private void HandleDied(Health _)
    {
        if (handledDeath)
        {
            return;
        }

        handledDeath = true;
        DeathPhysics.Disable(gameObject);
        PlayDeathEffect();

        if (authority != null && !authority.IsAuthoritative)
        {
            HideUntilDespawned();
            return;
        }

        if (TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
        {
            networkObject.Despawn();
            return;
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Turns off every renderer under the mob so a client shows only the death effect until the host's despawn
    /// arrives.
    /// </summary>
    private void HideUntilDespawned()
    {
        foreach (Renderer bodyRenderer in GetComponentsInChildren<Renderer>())
        {
            bodyRenderer.enabled = false;
        }
    }

    /// <summary>
    /// Plays the death effect from the pool with its feet where the mob stands, sorted there. Skipped without an
    /// effect, and with a warning when no pool was injected.
    /// </summary>
    private void PlayDeathEffect()
    {
        if (deathEffect == null)
        {
            return;
        }

        if (effects == null)
        {
            Debug.LogWarning($"{name} was not injected with an {nameof(EffectPool)}; skipping its death effect.", this);
            return;
        }

        Vector2 feet = transform.position;
        effects.Play(deathEffect, 0, 0, feet, feet.y);
    }

    /// <summary>
    /// Looks up the sibling <see cref="Health"/> when not already set.
    /// </summary>
    private void ResolveReferences()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
