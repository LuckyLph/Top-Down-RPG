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

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");

    private Rigidbody2D rb;
    private Vector2 desiredVelocity;
    private Vector2 lastMoveDirection = Vector2.down;

    public Vector2 Position => rb != null ? rb.position : (Vector2)transform.position;
    public float MoveSpeed => moveSpeed;

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
        if (!useHorizontalFlip || spriteRenderer == null)
        {
            return;
        }

        float deltaX = worldPosition.x - Position.x;
        if (Mathf.Abs(deltaX) > 0.01f)
        {
            spriteRenderer.flipX = deltaX < 0f;
        }
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
    }

    private void UpdateAnimator(Vector2 velocity)
    {
        if (animator == null)
        {
            return;
        }

        bool isMoving = velocity.sqrMagnitude > 0.0001f;
        if (isMoving)
        {
            lastMoveDirection = velocity.normalized;
        }

        Vector2 animationDirection = isMoving ? velocity.normalized : lastMoveDirection;

        animator.SetBool(IsMovingHash, isMoving);
        animator.SetFloat(MoveXHash, animationDirection.x);
        animator.SetFloat(MoveYHash, animationDirection.y);
        animator.SetFloat(LastMoveXHash, lastMoveDirection.x);
        animator.SetFloat(LastMoveYHash, lastMoveDirection.y);
        animator.speed = isMoving ? walkAnimationSpeed : 1f;
    }

    private void ResolveSpriteRenderer()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
    }
}

