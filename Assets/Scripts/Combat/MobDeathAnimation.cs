using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// A standalone copy of a mob's death animation, spawned by <see cref="DestroyMobOnDeath"/>: it matches the dead
/// body's sprite placement and look, plays its clip through a playable graph and destroys itself when the clip (or
/// the fallback lifetime) ends. The template on the mob is never initialised, so it does nothing.
/// </summary>
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

    /// <summary>
    /// Caches the sprite renderer, animator and optional <see cref="YPositionSorter"/>.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Caches the sprite renderer, animator and optional <see cref="YPositionSorter"/>.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Once initialised, restarts the lifetime and the clip each time the object is enabled.
    /// </summary>
    private void OnEnable()
    {
        if (!initialized)
        {
            return;
        }

        elapsedTime = 0f;
        StartDeathAnimation();
    }

    /// <summary>
    /// Stops the clip and releases its playable graph.
    /// </summary>
    private void OnDisable()
    {
        StopDeathAnimation();
    }

    /// <summary>
    /// Copies the dead body's position, rotation, material, color, flips, mask interaction and sorting (order only
    /// without a <see cref="YPositionSorter"/>) from <paramref name="sourceRenderer"/>, scales to 1.2 times its
    /// height and arms the lifetime; starts the clip right away if already enabled. Without a source it only arms
    /// the lifetime.
    /// </summary>
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

    /// <summary>
    /// Once initialised, destroys the object when its lifetime has elapsed.
    /// </summary>
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

    /// <summary>
    /// The clip's length, or the fallback lifetime when there is no clip or it is empty.
    /// </summary>
    private float ResolveLifetime()
    {
        if (deathAnimation != null && deathAnimation.length > 0f)
        {
            return deathAnimation.length;
        }

        return fallbackLifetime;
    }

    /// <summary>
    /// Looks up the sibling sprite renderer, animator and optional <see cref="YPositionSorter"/> when not cached.
    /// </summary>
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

    /// <summary>
    /// Replaces any running graph with a new one that plays the death clip on the animator from its first frame,
    /// even off screen. Does nothing without a clip or animator.
    /// </summary>
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

    /// <summary>
    /// Releases the playable graph if it is still alive.
    /// </summary>
    private void OnDestroy()
    {
        StopDeathAnimation();
    }

    /// <summary>
    /// Destroys the playable graph when one is valid.
    /// </summary>
    private void StopDeathAnimation()
    {
        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }
    }
}
