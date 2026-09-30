using UnityEngine;
using VContainer;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DestroyMobOnDeath : MonoBehaviour
{
    [SerializeField] private MobDeathAnimation deathAnimationTemplate;
    [SerializeField] private SpriteRenderer sourceSpriteRenderer;

    private Health health;
    private EffectSpawner effectSpawner;
    private bool handledDeath;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    [Inject]
    public void Construct(EffectSpawner spawner)
    {
        effectSpawner = spawner;
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (health != null)
        {
            health.Died += HandleDied;
        }
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
        DeathPhysics.Disable(gameObject);
        SpawnDeathAnimation();
        Destroy(gameObject);
    }

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
