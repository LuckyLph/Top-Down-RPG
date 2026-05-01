using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
public class MobDeathAnimation : MonoBehaviour
{
    [SerializeField] private AnimationClip deathAnimation;
    [SerializeField, Min(0.01f)] private float fallbackLifetime = 1f;

    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private YPositionSorter yPositionSorter;
    private PlayableGraph animationGraph;
    private float lifetime = 1f;
    private float elapsedTime;
    private bool initialized;

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
        if (!initialized)
        {
            return;
        }

        elapsedTime = 0f;
        StartDeathAnimation();
    }

    private void OnDisable()
    {
        StopDeathAnimation();
    }

    public void InitializeFrom(SpriteRenderer sourceRenderer)
    {
        ResolveReferences();

        if (sourceRenderer == null || spriteRenderer == null)
        {
            initialized = true;
            lifetime = ResolveLifetime();
            elapsedTime = 0f;
            return;
        }

        transform.position = sourceRenderer.transform.position;
        transform.rotation = sourceRenderer.transform.rotation;

        spriteRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        spriteRenderer.color = sourceRenderer.color;
        spriteRenderer.flipX = sourceRenderer.flipX;
        spriteRenderer.flipY = sourceRenderer.flipY;
        spriteRenderer.maskInteraction = sourceRenderer.maskInteraction;
        spriteRenderer.sortingLayerID = sourceRenderer.sortingLayerID;

        if (yPositionSorter == null)
        {
            spriteRenderer.sortingOrder = sourceRenderer.sortingOrder;
        }

        if (spriteRenderer.sprite != null && sourceRenderer.bounds.size.y > 0.0001f)
        {
            float spriteHeight = spriteRenderer.sprite.bounds.size.y;
            if (spriteHeight > 0.0001f)
            {
                float targetScale = sourceRenderer.bounds.size.y / spriteHeight;
                transform.localScale = new Vector3(1.2f, 1.2f, 1f) * targetScale;
            }
        }

        initialized = true;
        lifetime = ResolveLifetime();
        elapsedTime = 0f;

        if (isActiveAndEnabled)
        {
            StartDeathAnimation();
        }
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        elapsedTime += Time.deltaTime;

        if (elapsedTime >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private float ResolveLifetime()
    {
        if (deathAnimation != null && deathAnimation.length > 0f)
        {
            return deathAnimation.length;
        }

        return fallbackLifetime;
    }

    private void ResolveReferences()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (yPositionSorter == null)
        {
            yPositionSorter = GetComponent<YPositionSorter>();
        }
    }

    private void StartDeathAnimation()
    {
        StopDeathAnimation();

        if (deathAnimation == null || animator == null)
        {
            return;
        }

        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;

        animationGraph = PlayableGraph.Create($"{name}_DeathAnimation");
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(animationGraph, "Death", animator);
        AnimationClipPlayable playable = AnimationClipPlayable.Create(animationGraph, deathAnimation);
        playable.SetApplyFootIK(false);
        output.SetSourcePlayable(playable);
        animationGraph.Play();
        animationGraph.Evaluate(0f);
    }

    private void OnDestroy()
    {
        StopDeathAnimation();
    }

    private void StopDeathAnimation()
    {
        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }
    }
}
