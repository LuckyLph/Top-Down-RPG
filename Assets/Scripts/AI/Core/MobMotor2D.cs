using System;
using UnityEngine;
using VContainer;

[RequireComponent(typeof(Rigidbody2D))]
public class MobMotor2D : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 2.5f;
    [SerializeField, Min(0f)] private float acceleration = 20f;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0.1f)] private float walkAnimationSpeed = 0.85f;
    [SerializeField] private bool useHorizontalFlip = false;
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 0.18f;

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");
    private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");

    private Rigidbody2D rb;
    private IClock clock = UnityClock.Shared;
    private Vector2 desiredVelocity;
    private Vector2 steeringVelocity;
    private Vector2 lastMoveDirection = Vector2.down;
    private bool isAttackAnimationActive;
    private bool isMoving;
    private float speedScale = 1f;
    private bool remoteDriven;
    private bool remoteMoving;
    private float attackAnimationEndTime;
    private bool supportsAttackAnimation;
    private Animator resolvedAnimator;
    private RuntimeAnimatorController resolvedController;

    public event Action<Vector2> AttackAnimationPlayed;

    public Vector2 Position => rb != null ? rb.position : (Vector2)transform.position;
    public float MoveSpeed => moveSpeed;

    /// <summary>
    /// The speed the mob can move at right now: its move speed times the speed scale.
    /// </summary>
    public float CurrentMoveSpeed => moveSpeed * speedScale;
    public bool IsMoving => isMoving;
    public Vector2 FacingDirection => lastMoveDirection;

    internal Vector2 DesiredVelocity => desiredVelocity;
    internal bool SupportsAttackAnimation => supportsAttackAnimation;
    internal bool IsAttackAnimationActive => isAttackAnimationActive;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ResolveSpriteRenderer();
        ResolveAnimator();
    }

    private void OnValidate()
    {
        ResolveSpriteRenderer();
        ResolveAnimator();
    }

    private void Update()
    {
        UpdateAttackAnimation();
    }

    [Inject]
    public void Construct(IClock gameClock)
    {
        clock = gameClock ?? UnityClock.Shared;
    }

    public void Initialize(MobConfig config)
    {
        if (config != null)
        {
            moveSpeed = config.moveSpeed;
            acceleration = config.acceleration;
        }

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        ResolveSpriteRenderer();
        ResolveAnimator();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    public void SetDesiredVelocity(Vector2 velocity)
    {
        desiredVelocity = Vector2.ClampMagnitude(velocity, CurrentMoveSpeed);
    }

    /// <summary>
    /// Scales the move speed: below 1 slows the mob, 0 holds it still (separation included).
    /// </summary>
    public void SetSpeedScale(float scale)
    {
        speedScale = Mathf.Max(0f, scale);
    }

    public void Stop()
    {
        desiredVelocity = Vector2.zero;
    }

    public void SetSteeringVelocity(Vector2 velocity)
    {
        steeringVelocity = velocity;
    }

    public void FaceTowards(Vector2 worldPosition)
    {
        Vector2 direction = worldPosition - Position;
        if (direction.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = direction.normalized;
        }

        if (!useHorizontalFlip || spriteRenderer == null || isAttackAnimationActive)
        {
            return;
        }

        if (Mathf.Abs(direction.x) > 0.01f)
        {
            spriteRenderer.flipX = direction.x < 0f;
        }
    }

    public void PlayAttackAnimation(Vector2 direction)
    {
        ResolveAnimator();

        if (direction.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = direction.normalized;
        }

        if (animator == null)
        {
            return;
        }

        attackAnimationEndTime = clock.Time + attackAnimationDuration;
        isAttackAnimationActive = true;
        ApplyAttackAnimation(direction);
        AttackAnimationPlayed?.Invoke(lastMoveDirection);
    }

    public void ShowRemoteMovement(bool moving, Vector2 facing)
    {
        ResolveAnimator();
        remoteDriven = true;
        remoteMoving = moving;
        if (facing.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = facing.normalized;
        }

        if (useHorizontalFlip && spriteRenderer != null && Mathf.Abs(lastMoveDirection.x) > 0.01f)
        {
            spriteRenderer.flipX = lastMoveDirection.x < 0f;
        }

        if (!isAttackAnimationActive)
        {
            UpdateAnimator(Vector2.zero);
        }
    }

    public void FixedTick()
    {
        if (rb == null)
        {
            return;
        }

        float step = acceleration * Time.fixedDeltaTime;
        Vector2 targetVelocity = Vector2.ClampMagnitude(desiredVelocity + steeringVelocity, CurrentMoveSpeed);
        rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, targetVelocity, step);

        if (spriteRenderer != null)
        {
            if (useHorizontalFlip && Mathf.Abs(desiredVelocity.x) > 0.01f)
            {
                spriteRenderer.flipX = desiredVelocity.x < 0f;
            }
            else if (!useHorizontalFlip)
            {
                spriteRenderer.flipX = false;
            }
        }

        UpdateAnimator(rb.linearVelocity);
    }

    private void ResolveAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        if (animator == resolvedAnimator && controller == resolvedController)
        {
            return;
        }

        resolvedAnimator = animator;
        resolvedController = controller;
        supportsAttackAnimation = CanDriveAnimator() && AnimatorHasBoolParameter(IsAttackingHash);
    }

    private void UpdateAnimator(Vector2 velocity)
    {
        if (remoteDriven)
        {
            isMoving = remoteMoving;
        }
        else
        {
            bool intendsToMove = desiredVelocity.sqrMagnitude > 0.0001f;
            isMoving = intendsToMove && velocity.sqrMagnitude > 0.0001f;
            if (intendsToMove)
            {
                lastMoveDirection = desiredVelocity.normalized;
            }
        }

        if (!CanDriveAnimator())
        {
            return;
        }

        ApplyAnimatorMovement(isMoving, lastMoveDirection);
    }

    private void ResolveSpriteRenderer()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
    }

    internal void UpdateAttackAnimation()
    {
        if (!isAttackAnimationActive)
        {
            return;
        }

        if (clock.Time >= attackAnimationEndTime)
        {
            EndAttackAnimation();
            return;
        }

        ApplyAttackAnimation(lastMoveDirection);
    }

    private void ApplyAttackAnimation(Vector2 direction)
    {
        if (!CanDriveAnimator())
        {
            return;
        }

        Vector2 resolvedDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : lastMoveDirection;
        lastMoveDirection = resolvedDirection;

        ApplyAnimatorMovement(false, resolvedDirection);

        if (supportsAttackAnimation)
        {
            animator.SetBool(IsAttackingHash, true);
        }
    }

    private void EndAttackAnimation()
    {
        isAttackAnimationActive = false;

        if (animator == null)
        {
            return;
        }

        if (supportsAttackAnimation)
        {
            animator.SetBool(IsAttackingHash, false);
        }

        UpdateAnimator(rb != null ? rb.linearVelocity : desiredVelocity);
    }

    private void ApplyAnimatorMovement(bool isMoving, Vector2 animationDirection)
    {
        animator.SetBool(IsMovingHash, isMoving);
        animator.SetFloat(MoveXHash, animationDirection.x);
        animator.SetFloat(MoveYHash, animationDirection.y);
        animator.SetFloat(LastMoveXHash, lastMoveDirection.x);
        animator.SetFloat(LastMoveYHash, lastMoveDirection.y);
        animator.speed = isAttackAnimationActive ? 1f : (isMoving ? walkAnimationSpeed : 1f);
    }

    private bool AnimatorHasBoolParameter(int nameHash)
    {
        if (animator == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == nameHash)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanDriveAnimator()
    {
        return animator != null && animator.runtimeAnimatorController != null;
    }
}
