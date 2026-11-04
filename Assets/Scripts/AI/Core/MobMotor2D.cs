using UnityEngine;

/// <summary>
/// Moves a mob's Rigidbody2D toward its desired (path) velocity plus a steering velocity, clamped to
/// <see cref="CurrentMoveSpeed"/> and reached at the configured acceleration (a speed scale of 0 holds it still at
/// once), and feeds the mob's <see cref="CharacterVisuals"/> (Docs/Animation.md, Phase 7): the velocity and facing it
/// moves with, and its attacks on their timeline. On clients it shows the replicated move and facing and the host's
/// attacks instead. Its move speed and acceleration are its <see cref="MobConfig"/>'s, taken in
/// <see cref="Initialize"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MobMotor2D : MonoBehaviour
{
    [SerializeField, Tooltip("The character visuals on the mob's Visuals child, which draw its locomotion and attacks.")]
    private CharacterVisuals visuals;

    private float moveSpeed = 2.5f;
    private float acceleration = 20f;
    private Rigidbody2D rb;
    private Vector2 desiredVelocity;
    private Vector2 steeringVelocity;
    private Vector2 lastMoveDirection = Vector2.down;
    private bool isMoving;
    private float speedScale = 1f;
    private bool remoteDriven;
    private Vector2 remoteMove;
    private CharacterAnimationClip attackClip;

    public Vector2 Position => rb != null ? rb.position : (Vector2)transform.position;
    public float MoveSpeed => moveSpeed;

    /// <summary>
    /// The speed the mob can move at right now: its move speed times the speed scale.
    /// </summary>
    public float CurrentMoveSpeed => moveSpeed * speedScale;
    public bool IsMoving => isMoving;
    public Vector2 FacingDirection => lastMoveDirection;

    /// <summary>
    /// How the mob moves now as a share of its move speed: its actual velocity over the move speed while it moves on
    /// purpose, zero standing (drift from crowd separation included), the replicated value on a client. A slowed copy
    /// so shows the same gait as the host's mob.
    /// </summary>
    public Vector2 CurrentMove
    {
        get
        {
            if (remoteDriven)
            {
                return remoteMove;
            }

            return isMoving && moveSpeed > 0f && rb != null ? rb.linearVelocity / moveSpeed : Vector2.zero;
        }
    }

    public CharacterVisuals Visuals => visuals;
    internal Vector2 DesiredVelocity => desiredVelocity;

    /// <summary>
    /// Caches the rigidbody and finds the character visuals in the children when they are not assigned.
    /// </summary>
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ResolveVisuals();
    }

    /// <summary>
    /// Fills in the character visuals from the children when they are not assigned.
    /// </summary>
    private void OnValidate()
    {
        ResolveVisuals();
    }

    /// <summary>
    /// Takes the move speed and acceleration from <paramref name="config"/> (when given), looks its attack clip up in
    /// the visuals' set, and sets the body up for top-down movement: no gravity, no rotation, interpolated. Logs an
    /// error when the set lacks the clip the config names.
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

        ResolveVisuals();
        attackClip = null;
        if (config != null && visuals != null && visuals.AnimationSet != null && !string.IsNullOrEmpty(config.AttackClip))
        {
            attackClip = visuals.AnimationSet.Find(config.AttackClip);
            if (attackClip == null)
            {
                Debug.LogError($"{name}'s {nameof(CharacterAnimationSet)} has no clip '{config.AttackClip}' for its attack.", this);
            }
        }

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    /// <summary>
    /// Sets the velocity the mob wants to move at, clamped to <see cref="CurrentMoveSpeed"/>. It also decides the
    /// facing and whether the mob shows itself moving.
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
    /// the facing nor whether the mob shows itself moving.
    /// </summary>
    public void SetSteeringVelocity(Vector2 velocity)
    {
        steeringVelocity = velocity;
    }

    /// <summary>
    /// Turns the facing toward <paramref name="worldPosition"/>.
    /// </summary>
    public void FaceTowards(Vector2 worldPosition)
    {
        Vector2 direction = worldPosition - Position;
        if (direction.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = direction.normalized;
        }
    }

    /// <summary>
    /// Faces <paramref name="direction"/> and plays the mob's attack clip toward it from now, stretched to
    /// <paramref name="timeline"/>; with <paramref name="untilContact"/> (a client's copy of the host's attack) its
    /// windup holds its last frame until <see cref="ShowAttackContact"/>, at most the character's contact wait past the
    /// windup, after which the copy goes back to its locomotion.
    /// </summary>
    public void PlayAttack(Vector2 direction, in AttackTimeline timeline, bool untilContact)
    {
        if (direction.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = direction.normalized;
        }

        if (visuals != null && attackClip != null)
        {
            visuals.PlayAction(attackClip, lastMoveDirection, timeline, untilContact);
        }

        ShowMovement();
    }

    /// <summary>
    /// Plays the contact of an attack played until contact, from now.
    /// </summary>
    public void ShowAttackContact()
    {
        if (visuals != null)
        {
            visuals.ReachContact();
        }
    }

    /// <summary>
    /// Stops showing the attack at once.
    /// </summary>
    public void StopAttack()
    {
        if (visuals != null)
        {
            visuals.StopAction(CharacterActionKind.Attack);
        }
    }

    /// <summary>
    /// Shows replicated movement on a client: from now on the mob moves as <paramref name="move"/> (its share of the
    /// move speed) and faces <paramref name="facing"/>, instead of following its own body.
    /// </summary>
    public void ShowRemoteMovement(Vector2 move, Vector2 facing)
    {
        remoteDriven = true;
        remoteMove = move;
        isMoving = move.sqrMagnitude > 0.0001f;
        if (facing.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = facing.normalized;
        }

        ShowMovement();
    }

    /// <summary>
    /// Accelerates the body toward the desired plus steering velocity, clamped to <see cref="CurrentMoveSpeed"/> (held
    /// at rest when that is 0), then decides whether it moves and which way it faces and shows that. Called by the
    /// brain each physics step.
    /// </summary>
    public void FixedTick()
    {
        if (rb == null)
        {
            return;
        }

        if (CurrentMoveSpeed <= 0f)
        {
            rb.linearVelocity = Vector2.zero;
        }
        else
        {
            Vector2 targetVelocity = Vector2.ClampMagnitude(desiredVelocity + steeringVelocity, CurrentMoveSpeed);
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        }

        if (!remoteDriven)
        {
            bool intendsToMove = desiredVelocity.sqrMagnitude > 0.0001f;
            isMoving = intendsToMove && rb.linearVelocity.sqrMagnitude > 0.0001f;
            if (intendsToMove)
            {
                lastMoveDirection = desiredVelocity.normalized;
            }
        }

        ShowMovement();
    }

    /// <summary>
    /// Gives the visuals the velocity the mob shows itself moving at and its facing.
    /// </summary>
    private void ShowMovement()
    {
        if (visuals != null)
        {
            visuals.SetLocomotion(CurrentMove * moveSpeed, lastMoveDirection);
        }
    }

    /// <summary>
    /// Finds the character visuals in the children when none is assigned.
    /// </summary>
    private void ResolveVisuals()
    {
        if (visuals == null)
        {
            visuals = GetComponentInChildren<CharacterVisuals>(true);
        }
    }
}
