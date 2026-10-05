using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// One weapon swing: a short-lived trigger hitbox that follows its owner's sprite, offset toward the attack
/// direction, plays its slash clip and applies the weapon's <see cref="Hit"/> once to each unit it touches (never the
/// owner), then destroys itself when the clip or its lifetime ends.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class SwordSlashAttack : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private BoxCollider2D hitbox;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0)] private int damageAmount = 1;
    [SerializeField, Min(0.01f)] private float lifetime = 0.18f;
    [SerializeField] private AnimationClip slashAnimation;

    private readonly HashSet<DamageReceiver> hitReceivers = new();

    private Transform ownerRoot;
    private Transform ownerAnchor;
    private GameObject damageSource;
    private HitService hitService;
    private Hit hit;
    private Vector2 direction = Vector2.down;
    private Vector2 spawnOffset;
    private float spawnDistance;
    private float activeLifetime;
    private float elapsedTime;
    private Vector3 rootBaseScale = Vector3.one;
    private PlayableGraph animationGraph;

    public Transform OwnerRoot => ownerRoot;
    public Vector2 Direction => direction;
    public int DamageAmount => damageAmount;
    public BoxCollider2D Hitbox => hitbox;
    public SpriteRenderer SpriteRenderer => spriteRenderer;

    /// <summary>
    /// Finds the hitbox, visual, sprite renderer and animator when unassigned, records the unmirrored scale and
    /// sets the lifetime.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        CacheRootBaseScale();
        activeLifetime = ResolveLifetime();
    }

    /// <summary>
    /// Fills in missing references, records the unmirrored scale and keeps the hitbox a trigger.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
        CacheRootBaseScale();

        if (hitbox != null)
        {
            hitbox.isTrigger = true;
        }
    }

    /// <summary>
    /// Advances the slash by the frame's delta time.
    /// </summary>
    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Ages the slash by <paramref name="deltaTime"/>, keeps it at its place beside the owner, and destroys it once
    /// its lifetime is over (immediately when the lifetime is not positive).
    /// </summary>
    internal void Tick(float deltaTime)
    {
        if (activeLifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        elapsedTime += deltaTime;
        UpdateFollowPosition();

        if (elapsedTime >= activeLifetime)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Tries to hit whatever enters the hitbox.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamageCollider(other);
    }

    /// <summary>
    /// Wires the visual, sprite renderer, hitbox and animator (and the clip, when given) without the inspector, for
    /// the editor prefab bootstrap and tests.
    /// </summary>
    public void ConfigureReferences(Transform visualTransform, SpriteRenderer renderer, BoxCollider2D boxCollider, Animator runtimeAnimator, AnimationClip animationClip = null)
    {
        visualRoot = visualTransform;
        spriteRenderer = renderer;
        hitbox = boxCollider;
        animator = runtimeAnimator;
        CacheRootBaseScale();
        if (animationClip != null)
        {
            slashAnimation = animationClip;
        }
    }

    /// <summary>
    /// Starts the swing for <paramref name="owner"/> toward <paramref name="attackDirection"/> (down when zero) with
    /// <paramref name="weapon"/>'s hit and placement: rotates and mirrors it, places it beside the owner's sprite,
    /// sorts it against <paramref name="ownerSpriteRenderer"/> when given, plays the clip, ignores the owner's
    /// colliders and immediately hits units already inside the hitbox.
    /// </summary>
    public void Initialize(Transform owner, PlayerWeapon weapon, Vector2 attackDirection, HitService hits, SpriteRenderer ownerSpriteRenderer = null)
    {
        ResolveReferences();

        hitService = hits;
        ownerRoot = owner;
        damageSource = owner != null ? owner.gameObject : gameObject;
        direction = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : Vector2.down;
        damageAmount = weapon != null ? weapon.Damage : damageAmount;
        hit = weapon != null ? weapon.Hit : new Hit(damageAmount, DamageType.Physical);
        activeLifetime = ResolveLifetime();
        spawnOffset = weapon != null ? weapon.GetSlashSpawnOffset(direction) : Vector2.zero;
        spawnDistance = weapon != null ? weapon.SlashSpawnDistance : 0f;
        ownerAnchor = ResolveOwnerAnchor(owner, ownerSpriteRenderer);

        Quaternion rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, direction));
        transform.rotation = rotation;
        ApplyDirectionalRootMirror();
        UpdateFollowPosition();

        if (hitbox != null)
        {
            hitbox.isTrigger = true;
        }

        MatchOwnerSorting(ownerSpriteRenderer);
        hitReceivers.Clear();
        elapsedTime = 0f;
        StartSlashAnimation();
        IgnoreOwnerCollisions();
        ProcessInitialOverlaps();
    }

    /// <summary>
    /// Applies the hit, with the owner as the source, to the unit <paramref name="other"/> belongs to. Returns false
    /// for the owner, a non-unit, a unit this slash already hit, a refused hit, or before
    /// <see cref="Initialize"/>; a unit only counts as hit once the hit reached it.
    /// </summary>
    public bool TryDamageCollider(Collider2D other)
    {
        if (other == null || hitService == null)
        {
            return false;
        }

        if (ownerRoot != null)
        {
            if (other.transform == ownerRoot || other.transform.IsChildOf(ownerRoot))
            {
                return false;
            }
        }

        DamageReceiver receiver = DamageReceiver.FindFor(other.transform);
        if (receiver == null || hitReceivers.Contains(receiver))
        {
            return false;
        }

        if (!hitService.ApplyHit(receiver, hit, damageSource))
        {
            return false;
        }

        hitReceivers.Add(receiver);
        return true;
    }

    /// <summary>
    /// Fills in the sibling hitbox and animator, the child named "Visual" and its sprite renderer, each only when
    /// not already set.
    /// </summary>
    private void ResolveReferences()
    {
        if (hitbox == null)
        {
            hitbox = GetComponent<BoxCollider2D>();
        }

        if (visualRoot == null)
        {
            Transform visual = transform.Find("Visual");
            if (visual != null)
            {
                visualRoot = visual;
            }
        }

        if (spriteRenderer == null && visualRoot != null)
        {
            spriteRenderer = visualRoot.GetComponent<SpriteRenderer>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    /// <summary>
    /// Records the current local scale with a positive X as the unmirrored scale.
    /// </summary>
    private void CacheRootBaseScale()
    {
        rootBaseScale = transform.localScale;
        rootBaseScale.x = Mathf.Abs(rootBaseScale.x);
    }

    /// <summary>
    /// Puts the slash on the owner sprite's sorting layer, one order behind it for upward swings and one in front
    /// otherwise.
    /// </summary>
    private void MatchOwnerSorting(SpriteRenderer ownerSpriteRenderer)
    {
        if (spriteRenderer == null || ownerSpriteRenderer == null)
        {
            return;
        }

        spriteRenderer.sortingLayerID = ownerSpriteRenderer.sortingLayerID;
        spriteRenderer.sortingOrder = ownerSpriteRenderer.sortingOrder + (IsNorthDirection(direction) ? -1 : 1);
    }

    /// <summary>
    /// Moves the slash to the owner's anchor (its sprite, else its root, else where it is) plus the spawn distance
    /// along the attack direction and the weapon's directional offset.
    /// </summary>
    private void UpdateFollowPosition()
    {
        Vector3 origin = ownerAnchor != null ? ownerAnchor.position : (ownerRoot != null ? ownerRoot.position : transform.position);
        Vector2 directionalOffset = direction * spawnDistance;
        transform.position = origin + (Vector3)(directionalOffset + spawnOffset);
    }

    /// <summary>
    /// The slash clip's length, or the configured lifetime (at least 0.01 s) without a usable clip.
    /// </summary>
    private float ResolveLifetime()
    {
        if (slashAnimation != null && slashAnimation.length > 0f)
        {
            return slashAnimation.length;
        }

        return Mathf.Max(0.01f, lifetime);
    }

    /// <summary>
    /// Mirrors the slash horizontally for eastward swings and restores the unmirrored scale otherwise.
    /// </summary>
    private void ApplyDirectionalRootMirror()
    {
        CacheRootBaseScale();
        Vector3 scale = rootBaseScale;
        if (IsEastDirection(direction))
        {
            scale.x = -scale.x;
        }

        transform.localScale = scale;
    }

    /// <summary>
    /// Whether <paramref name="attackDirection"/> points right with the horizontal component strictly dominant.
    /// </summary>
    private static bool IsEastDirection(Vector2 attackDirection)
    {
        return attackDirection.x > 0f && Mathf.Abs(attackDirection.x) > Mathf.Abs(attackDirection.y);
    }

    /// <summary>
    /// Whether <paramref name="attackDirection"/> points up with the vertical component at least as large as the
    /// horizontal one.
    /// </summary>
    private static bool IsNorthDirection(Vector2 attackDirection)
    {
        return attackDirection.y > 0f && attackDirection.y >= Mathf.Abs(attackDirection.x);
    }

    /// <summary>
    /// The transform the slash follows: the given owner sprite, else the owner's first child sprite renderer
    /// (inactive included), else the owner itself; null without an owner.
    /// </summary>
    private static Transform ResolveOwnerAnchor(Transform owner, SpriteRenderer ownerSpriteRenderer)
    {
        if (ownerSpriteRenderer != null)
        {
            return ownerSpriteRenderer.transform;
        }

        if (owner == null)
        {
            return null;
        }

        SpriteRenderer childRenderer = owner.GetComponentInChildren<SpriteRenderer>(true);
        if (childRenderer != null)
        {
            return childRenderer.transform;
        }

        return owner;
    }

    /// <summary>
    /// Makes physics ignore contacts between the hitbox and every collider under the owner.
    /// </summary>
    private void IgnoreOwnerCollisions()
    {
        if (ownerRoot == null || hitbox == null)
        {
            return;
        }

        Collider2D[] ownerColliders = ownerRoot.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < ownerColliders.Length; i++)
        {
            Collider2D ownerCollider = ownerColliders[i];
            if (ownerCollider == null || ownerCollider == hitbox)
            {
                continue;
            }

            Physics2D.IgnoreCollision(hitbox, ownerCollider, true);
        }
    }

    /// <summary>
    /// Syncs transforms and tries to hit every collider already overlapping the hitbox, since trigger enter only
    /// reports later contacts.
    /// </summary>
    private void ProcessInitialOverlaps()
    {
        if (hitbox == null)
        {
            return;
        }

        Physics2D.SyncTransforms();
        Vector2 worldCenter = (Vector2)transform.position + (Vector2)(transform.rotation * hitbox.offset);
        Collider2D[] overlaps = Physics2D.OverlapBoxAll(worldCenter, hitbox.size, transform.eulerAngles.z);
        for (int i = 0; i < overlaps.Length; i++)
        {
            TryDamageCollider(overlaps[i]);
        }
    }

    /// <summary>
    /// Replaces any running graph with a new one that plays the slash clip on the animator from its first frame,
    /// even off screen. Does nothing without a clip or animator.
    /// </summary>
    private void StartSlashAnimation()
    {
        StopSlashAnimation();

        if (slashAnimation == null || animator == null)
        {
            return;
        }

        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;

        animationGraph = PlayableGraph.Create($"{name}_SlashAnimation");
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(animationGraph, "Slash", animator);
        AnimationClipPlayable playable = AnimationClipPlayable.Create(animationGraph, slashAnimation);
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
        StopSlashAnimation();
    }

    /// <summary>
    /// Destroys the playable graph when one is valid.
    /// </summary>
    private void StopSlashAnimation()
    {
        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }
    }
}
