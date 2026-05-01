using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

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

    private void Awake()
    {
        ResolveReferences();
        CacheRootBaseScale();
        activeLifetime = ResolveLifetime();
    }

    private void OnValidate()
    {
        ResolveReferences();
        CacheRootBaseScale();

        if (hitbox != null)
        {
            hitbox.isTrigger = true;
        }
    }

    private void Update()
    {
        if (activeLifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        elapsedTime += Time.deltaTime;
        UpdateFollowPosition();

        if (elapsedTime >= activeLifetime)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamageCollider(other);
    }

    public static SwordSlashAttack Spawn(Transform owner, PlayerWeapon weapon, Vector2 attackDirection, SpriteRenderer ownerSpriteRenderer = null)
    {
        SwordSlashAttack slashAttack = CreateSlashInstance(weapon);
        slashAttack.gameObject.name = $"{(weapon != null ? weapon.DisplayName : "Player")}Slash";
        slashAttack.Initialize(owner, weapon, attackDirection, ownerSpriteRenderer);
        return slashAttack;
    }

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

    public void Initialize(Transform owner, PlayerWeapon weapon, Vector2 attackDirection, SpriteRenderer ownerSpriteRenderer = null)
    {
        ResolveReferences();

        ownerRoot = owner;
        damageSource = owner != null ? owner.gameObject : gameObject;
        direction = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : Vector2.down;
        damageAmount = weapon != null ? weapon.Damage : damageAmount;
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

    public bool TryDamageCollider(Collider2D other)
    {
        if (other == null)
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

        DamageReceiver receiver = ResolveReceiver(other.transform);
        if (receiver == null || hitReceivers.Contains(receiver))
        {
            return false;
        }

        int appliedDamage = receiver.ReceiveDamage(damageAmount, damageSource);
        if (appliedDamage <= 0)
        {
            return false;
        }

        hitReceivers.Add(receiver);
        return true;
    }

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

    private void CacheRootBaseScale()
    {
        rootBaseScale = transform.localScale;
        rootBaseScale.x = Mathf.Abs(rootBaseScale.x);
    }

    private static SwordSlashAttack CreateSlashInstance(PlayerWeapon weapon)
    {
        if (weapon != null && weapon.SlashPrefab != null)
        {
            GameObject slashObject = Instantiate(weapon.SlashPrefab);
            slashObject.SetActive(true);

            SwordSlashAttack slashAttack = slashObject.GetComponent<SwordSlashAttack>();
            if (slashAttack != null)
            {
                return slashAttack;
            }

            slashAttack = slashObject.GetComponentInChildren<SwordSlashAttack>(true);
            if (slashAttack != null)
            {
                return slashAttack;
            }

            Destroy(slashObject);
        }

        return CreateFallbackSlashInstance(weapon);
    }

    private static SwordSlashAttack CreateFallbackSlashInstance(PlayerWeapon weapon)
    {
        GameObject root = new($"{(weapon != null ? weapon.DisplayName : "Player")}Slash");
        BoxCollider2D boxCollider = root.AddComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;
        boxCollider.offset = new Vector2(0f, 0.1f);
        boxCollider.size = new Vector2(0.9f, 0.9f);

        Animator runtimeAnimator = root.AddComponent<Animator>();
        SwordSlashAttack slashAttack = root.AddComponent<SwordSlashAttack>();

        GameObject visualObject = new("Visual");
        visualObject.transform.SetParent(root.transform, false);
        visualObject.transform.localScale = Vector3.one * 0.14f;
        SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();

        slashAttack.ConfigureReferences(visualObject.transform, renderer, boxCollider, runtimeAnimator);
        return slashAttack;
    }

    private void MatchOwnerSorting(SpriteRenderer ownerSpriteRenderer)
    {
        if (spriteRenderer == null || ownerSpriteRenderer == null)
        {
            return;
        }

        spriteRenderer.sortingLayerID = ownerSpriteRenderer.sortingLayerID;
        spriteRenderer.sortingOrder = ownerSpriteRenderer.sortingOrder + 1;
    }

    private void UpdateFollowPosition()
    {
        Vector3 origin = ownerAnchor != null ? ownerAnchor.position : (ownerRoot != null ? ownerRoot.position : transform.position);
        Vector2 directionalOffset = direction * spawnDistance;
        transform.position = origin + (Vector3)(directionalOffset + spawnOffset);
    }

    private float ResolveLifetime()
    {
        if (slashAnimation != null && slashAnimation.length > 0f)
        {
            return slashAnimation.length;
        }

        return Mathf.Max(0.01f, lifetime);
    }

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

    private static bool IsEastDirection(Vector2 attackDirection)
    {
        return attackDirection.x > 0f && Mathf.Abs(attackDirection.x) > Mathf.Abs(attackDirection.y);
    }

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

    private void OnDestroy()
    {
        StopSlashAnimation();
    }

    private void StopSlashAnimation()
    {
        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }
    }

    private static DamageReceiver ResolveReceiver(Transform target)
    {
        if (target == null)
        {
            return null;
        }

        if (target.TryGetComponent(out DamageReceiver receiver))
        {
            return receiver;
        }

        receiver = target.GetComponentInParent<DamageReceiver>();
        if (receiver != null)
        {
            return receiver;
        }

        Transform root = target.root;
        return root != null ? root.GetComponentInChildren<DamageReceiver>(true) : null;
    }
}
