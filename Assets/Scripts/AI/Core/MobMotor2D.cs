using UnityEngine;

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
    private Vector2 desiredVelocity;
    private Vector2 lastMoveDirection = Vector2.down;
    private bool isAttackAnimationActive;
    private float attackAnimationEndTime;
    private bool supportsAttackAnimation;

    public Vector2 Position => rb != null ? rb.position : (Vector2)transform.position;
    public float MoveSpeed => moveSpeed;

    internal bool SupportsAttackAnimation => supportsAttackAnimation;
    internal bool IsAttackAnimationActive => isAttackAnimationActive;
    internal Vector2 LastMoveDirection => lastMoveDirection;

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
        desiredVelocity = Vector2.ClampMagnitude(velocity, moveSpeed);
    }

    public void Stop()
    {
        desiredVelocity = Vector2.zero;
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

        attackAnimationEndTime = Time.time + attackAnimationDuration;
        isAttackAnimationActive = true;
        ApplyAttackAnimation(direction);
    }

    public void FixedTick()
    {
        if (rb == null)
        {
            return;
        }

        float step = acceleration * Time.fixedDeltaTime;
        rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, desiredVelocity, step);

        if (spriteRenderer != null)
        {
            if (useHorizontalFlip && Mathf.Abs(rb.linearVelocity.x) > 0.01f)
            {
                spriteRenderer.flipX = rb.linearVelocity.x < 0f;
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

        supportsAttackAnimation = CanDriveAnimator() && AnimatorHasBoolParameter(IsAttackingHash);
    }

    private void UpdateAnimator(Vector2 velocity)
    {
        bool isMoving = velocity.sqrMagnitude > 0.0001f;
        if (isMoving)
        {
            lastMoveDirection = velocity.normalized;
        }

        if (!CanDriveAnimator())
        {
            return;
        }

        Vector2 animationDirection = isMoving ? velocity.normalized : lastMoveDirection;
        ApplyAnimatorMovement(isMoving, animationDirection);
    }

    private void ResolveSpriteRenderer()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
    }

    private void UpdateAttackAnimation()
    {
        if (!isAttackAnimationActive)
        {
            return;
        }

        if (Time.time >= attackAnimationEndTime)
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
