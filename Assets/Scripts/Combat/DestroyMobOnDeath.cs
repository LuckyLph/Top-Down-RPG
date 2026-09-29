using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DestroyMobOnDeath : MonoBehaviour
{
    [SerializeField] private MobDeathAnimation deathAnimationTemplate;
    [SerializeField] private SpriteRenderer sourceSpriteRenderer;

    private Health health;
    private bool handledDeath;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
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

        GameObject instanceObject = Instantiate(deathAnimationTemplate.gameObject);
        instanceObject.name = $"{gameObject.name} Death Animation";
        instanceObject.transform.SetParent(null, false);
        instanceObject.SetActive(false);

        MobDeathAnimation instance = instanceObject.GetComponent<MobDeathAnimation>();
        if (instance != null)
        {
            instance.InitializeFrom(sourceSpriteRenderer);
        }

        instanceObject.SetActive(true);
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
