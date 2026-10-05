using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// A mob's death reaction: on the first death it turns off the mob's physics and spawns its death animation, then
/// the authority despawns the network object (or destroys a non-networked mob) while other machines hide the body
/// until the despawn reaches them.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DestroyMobOnDeath : MonoBehaviour
{
    [SerializeField] private MobDeathAnimation deathAnimationTemplate;
    [SerializeField] private SpriteRenderer sourceSpriteRenderer;

    private Health health;
    private EffectSpawner effectSpawner;
    private IGameAuthority authority;
    private bool handledDeath;

    /// <summary>
    /// Finds the health, death animation template and body sprite renderer when unassigned.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Fills in the health, death animation template and body sprite renderer when unassigned.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Injects the spawner the death animation is played through and the authority that decides who removes the mob.
    /// </summary>
    [Inject]
    public void Construct(EffectSpawner spawner, IGameAuthority gameAuthority)
    {
        effectSpawner = spawner;
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
    /// Runs once per mob: disables its physics and spawns the death animation; then a non-authoritative machine
    /// hides the body, while the authority despawns the spawned network object or destroys the game object.
    /// </summary>
    private void HandleDied(Health _)
    {
        if (handledDeath)
        {
            return;
        }

        handledDeath = true;
        DeathPhysics.Disable(gameObject);
        SpawnDeathAnimation();

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
    /// Turns off every renderer under the mob so a client shows only the death animation until the host's despawn
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
    /// Spawns a copy of the death animation template through the <see cref="EffectSpawner"/>, initialised from the
    /// body sprite. Skipped without a template, and with a warning when no spawner was injected.
    /// </summary>
    private void SpawnDeathAnimation()
    {
        if (deathAnimationTemplate == null)
        {
            return;
        }

        if (effectSpawner == null)
        {
            Debug.LogWarning($"{name} was not injected with an {nameof(EffectSpawner)}; skipping its death animation.", this);
            return;
        }

        string instanceName = $"{gameObject.name} Death Animation";
        effectSpawner.Spawn(deathAnimationTemplate, instance =>
        {
            instance.name = instanceName;
            instance.InitializeFrom(sourceSpriteRenderer);
        });
    }

    /// <summary>
    /// Looks up the sibling <see cref="Health"/>, the child <see cref="MobDeathAnimation"/> template and the first
    /// child sprite renderer outside that template, each only when not already set.
    /// </summary>
    private void ResolveReferences()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }

        if (deathAnimationTemplate == null)
        {
            deathAnimationTemplate = GetComponentInChildren<MobDeathAnimation>(true);
        }

        if (sourceSpriteRenderer == null)
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer candidate = renderers[i];
                if (candidate == null)
                {
                    continue;
                }

                if (deathAnimationTemplate != null && candidate.transform.IsChildOf(deathAnimationTemplate.transform))
                {
                    continue;
                }

                sourceSpriteRenderer = candidate;
                break;
            }
        }
    }
}
