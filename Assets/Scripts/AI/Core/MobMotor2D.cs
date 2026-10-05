using System;
using UnityEngine;
using VContainer;

/// <summary>
/// Moves a mob's Rigidbody2D toward its desired (path) velocity plus a steering velocity, clamped to
/// <see cref="CurrentMoveSpeed"/> and reached at the configured acceleration. Drives the animator's movement
/// parameters and an optional timed attack animation, or, on clients, shows replicated movement instead.
/// </summary>
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

    /// <summary>
    /// Caches the rigidbody and finds the sprite renderer and animator in the children when they are not assigned.
    /// </summary>
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ResolveSpriteRenderer();
        ResolveAnimator();
    }

    /// <summary>
    /// Fills in the sprite renderer and animator from the children when they are not assigned.
    /// </summary>
    private void OnValidate()
    {
        ResolveSpriteRenderer();
        ResolveAnimator();
    }

    /// <summary>
    /// Advances the attack animation: ends it once its duration has passed on the clock.
    /// </summary>
    private void Update()
    {
        UpdateAttackAnimation();
    }

    /// <summary>
    /// Injects the game clock that times attack animations; falls back to <see cref="UnityClock.Shared"/> when null.
    /// </summary>
    [Inject]
    public void Construct(IClock gameClock)
    {
        clock = gameClock ?? UnityClock.Shared;
    }

    /// <summary>
    /// Takes the move speed and acceleration from <paramref name="config"/> (when given) and sets the body up for
    /// top-down movement: no gravity, no rotation, interpolated.
    /// </summary>
    public void Initialize(MobConfig config)
    {
        if (config != null)
        {
            moveSpeed = config.MoveSpeed;
            acceleration = config.Acceleration;
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

    /// <summary>
    /// Sets the velocity the mob wants to move at, clamped to <see cref="CurrentMoveSpeed"/>. It also decides the
    /// facing and whether the walk animation plays.
    /// </summary>
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

    /// <summary>
    /// Clears the desired velocity; the body slows down at the acceleration, and the steering velocity still applies.
    /// </summary>
    public void Stop()
    {
        desiredVelocity = Vector2.zero;
    }

    /// <summary>
    /// Sets the extra velocity added to the desired one each fixed tick, such as crowd separation. It changes neither
    /// the facing nor the walk animation.
    /// </summary>
    public void SetSteeringVelocity(Vector2 velocity)
    {
        steeringVelocity = velocity;
    }

    /// <summary>
    /// Turns the facing toward <paramref name="worldPosition"/>, and flips the sprite to match when horizontal flip is
    /// on and no attack animation is playing.
    /// </summary>
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

    /// <summary>
    /// Faces <paramref name="direction"/> and holds the attack pose for the configured duration on the clock, then
    /// raises <see cref="AttackAnimationPlayed"/>. Only changes the facing when the mob has no animator.
    /// </summary>
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

    /// <summary>
    /// Shows replicated movement on a client: faces <paramref name="facing"/> and, from now on, plays the walk
    /// animation from <paramref name="moving"/> instead of the body's own velocity.
    /// </summary>
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

    /// <summary>
    /// Accelerates the body toward the desired plus steering velocity, clamped to <see cref="CurrentMoveSpeed"/>, flips
    /// the sprite toward the desired direction and updates the animator. Called by the brain each physics step.
    /// </summary>
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

    /// <summary>
    /// Finds the animator in the children when none is assigned and, when the animator or its controller changed,
    /// checks again whether it has an IsAttacking bool.
    /// </summary>
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

    /// <summary>
    /// Updates <see cref="IsMoving"/> and the facing, from the replicated state when remote driven, else from the
    /// desired velocity and the body's <paramref name="velocity"/>, and writes them to the animator.
    /// </summary>
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

    /// <summary>
    /// Finds the sprite renderer in the children when none is assigned.
    /// </summary>
    private void ResolveSpriteRenderer()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
    }

    /// <summary>
    /// Ends the attack animation once its end time has passed on the clock, otherwise keeps its pose applied. Runs
    /// every frame; internal so tests can step it.
    /// </summary>
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

    /// <summary>
    /// Applies the attack pose: no walking, facing <paramref name="direction"/> (or the last facing when it is zero),
    /// and IsAttacking set when the controller has it.
    /// </summary>
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

    /// <summary>
    /// Ends the attack animation, clears IsAttacking and puts the animator back on the body's movement.
    /// </summary>
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

    /// <summary>
    /// Writes the movement and last-facing parameters, and sets the animator speed to the walk speed while walking and
    /// to normal speed otherwise or while attacking.
    /// </summary>
    private void ApplyAnimatorMovement(bool isMoving, Vector2 animationDirection)
    {
        animator.SetBool(IsMovingHash, isMoving);
        animator.SetFloat(MoveXHash, animationDirection.x);
        animator.SetFloat(MoveYHash, animationDirection.y);
        animator.SetFloat(LastMoveXHash, lastMoveDirection.x);
        animator.SetFloat(LastMoveYHash, lastMoveDirection.y);
        animator.speed = isAttackAnimationActive ? 1f : (isMoving ? walkAnimationSpeed : 1f);
    }

    /// <summary>
    /// Whether the animator has a bool parameter whose name hashes to <paramref name="nameHash"/>.
    /// </summary>
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

    /// <summary>
    /// Whether there is an animator with a controller to set parameters on.
    /// </summary>
    private bool CanDriveAnimator()
    {
        return animator != null && animator.runtimeAnimatorController != null;
    }
}
