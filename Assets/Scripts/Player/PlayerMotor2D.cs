using UnityEngine;
using VContainer;

/// <summary>
/// Moves the player's <see cref="Rigidbody2D"/> along a <see cref="PathFollower2D"/> path on its own navigation, its
/// world's or the area's (<see cref="PlayerNavigation"/>; a straight line when there is none) at the settings' speed times a speed scale (slows; 0 while stunned or
/// rooted, which also keeps stall time from growing), tracks facing and stall time, and drives the animator. Remote
/// copies show the owner's replicated movement instead.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMotor2D : MonoBehaviour
{
    private const float MovingThreshold = 0.0001f;
    private const float GroundSearchStep = 0.5f;

    [SerializeField, Min(0.1f)] private float walkAnimationSpeed = 0.85f;
    [SerializeField] private Animator animator;

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");

    private readonly PathFollower2D follower = new();
    private Rigidbody2D rb;
    private PlayerNavigation playerNavigation;
    private PlayerControlSettings settings;
    private INavigation2D configuredNavigation;
    private bool followerConfigured;
    private Vector2 currentMove;
    private Vector2 facing = Vector2.down;
    private bool simulatesMovement = true;
    private bool aiming;
    private float speedScale = 1f;

    public Vector2 CurrentMove => currentMove;
    public Vector2 FacingDirection => facing;
    public bool HasPath => follower.HasPath;
    public bool ReachedDestination => follower.ReachedDestination;
    public float StalledTime => follower.StalledTime;
    public Vector2 Position => Body != null ? Body.position : (Vector2)transform.position;

    internal PathFollower2D Follower => follower;
    internal float SpeedScale => speedScale;

    private Rigidbody2D Body
    {
        get
        {
            if (rb == null)
            {
                rb = GetComponent<Rigidbody2D>();
            }

            return rb;
        }
    }

    /// <summary>
    /// Finds the animator and sets the body up for top-down movement: no gravity, no rotation, interpolated.
    /// </summary>
    private void Awake()
    {
        ResolveAnimator();
        Rigidbody2D body = Body;
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    /// <summary>
    /// Fills an empty animator reference from the children, inactive ones included.
    /// </summary>
    private void OnValidate()
    {
        ResolveAnimator();
    }

    /// <summary>
    /// Drops the path and stops the body.
    /// </summary>
    private void OnDisable()
    {
        Stop();
    }

    /// <summary>
    /// Pushes movement and facing to the animator each frame.
    /// </summary>
    private void Update()
    {
        UpdateAnimator();
    }

    /// <summary>
    /// Runs one <see cref="FixedTick"/> per physics step.
    /// </summary>
    private void FixedUpdate()
    {
        FixedTick(Time.fixedDeltaTime);
    }

    /// <summary>
    /// Takes the lookup of the navigation this player paths on: its world's, or the area's.
    /// </summary>
    [Inject]
    public void Construct(PlayerNavigation navigation)
    {
        playerNavigation = navigation;
    }

    /// <summary>
    /// Takes the control settings (speed, path following, terrain profile) and drops any path; the path follower is
    /// reconfigured on the next <see cref="MoveTo"/>.
    /// </summary>
    public void Initialize(PlayerControlSettings controlSettings)
    {
        settings = controlSettings;
        followerConfigured = false;
        follower.Clear();
    }

    /// <summary>
    /// Builds a path from the body to <paramref name="destination"/> on the player's navigation (an unwalkable or
    /// unreachable destination ends at the closest walkable cell, one beyond the loaded chunks of a world at the loaded
    /// edge), or a straight line without one.
    /// </summary>
    public bool MoveTo(Vector2 destination)
    {
        if (settings == null || !simulatesMovement)
        {
            return false;
        }

        INavigation2D navigation = playerNavigation != null ? playerNavigation.Of(transform) : null;
        if (!followerConfigured || !ReferenceEquals(navigation, configuredNavigation))
        {
            follower.Configure(navigation, settings.MovementProfile, settings.FollowerSettings);
            configuredNavigation = navigation;
            followerConfigured = true;
        }

        if (follower.IsConfigured)
        {
            return follower.BuildPath(Position, destination, allowPartial: true)
                || BuildTowardWalkableGround(navigation, destination);
        }

        follower.BuildStraightPath(Position, destination);
        return true;
    }

    /// <summary>
    /// Drops the path and the move direction, and zeroes the body's velocity when this copy simulates movement.
    /// </summary>
    public void Stop()
    {
        follower.Clear();
        currentMove = Vector2.zero;
        if (simulatesMovement && Body != null)
        {
            Body.linearVelocity = Vector2.zero;
        }
    }

    /// <summary>
    /// Faces <paramref name="direction"/>; a near-zero direction keeps the current facing.
    /// </summary>
    public void Face(Vector2 direction)
    {
        if (direction.sqrMagnitude > MovingThreshold)
        {
            facing = direction.normalized;
        }
    }

    /// <summary>
    /// Scales the move speed: below 1 slows the player, 0 holds it still while its path and order are kept.
    /// </summary>
    public void SetSpeedScale(float scale)
    {
        speedScale = Mathf.Max(0f, scale);
    }

    /// <summary>
    /// While aiming, the player faces <paramref name="direction"/> and movement no longer turns it.
    /// </summary>
    public void SetAim(bool aim, Vector2 direction)
    {
        aiming = aim;
        if (aim)
        {
            Face(direction);
        }
    }

    /// <summary>
    /// Whether this copy moves the body itself; turning it off stops first, then ignores move requests and physics
    /// ticks so only <see cref="ShowRemoteMovement"/> drives it.
    /// </summary>
    public void SetSimulatesMovement(bool simulates)
    {
        if (!simulates)
        {
            Stop();
        }

        simulatesMovement = simulates;
    }

    /// <summary>
    /// Shows another machine's movement on this copy: the owner's move direction and facing drive the animator.
    /// </summary>
    public void ShowRemoteMovement(Vector2 move, Vector2 remoteFacing)
    {
        currentMove = move;
        Face(remoteFacing);
    }

    /// <summary>
    /// Stops and places both the body and the transform at <paramref name="position"/>, keeping the transform's z.
    /// </summary>
    public void Teleport(Vector2 position)
    {
        Stop();
        Rigidbody2D body = Body;
        body.position = position;
        body.linearVelocity = Vector2.zero;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    /// <summary>
    /// One physics step: advances the path at the scaled speed, sets the body's velocity, records the move as a
    /// fraction of that speed and, unless aiming, faces along it. Does nothing on a remote copy or before
    /// <see cref="Initialize"/>.
    /// </summary>
    internal void FixedTick(float deltaTime)
    {
        if (!simulatesMovement || settings == null)
        {
            return;
        }

        float speed = settings.MoveSpeed * speedScale;
        Vector2 velocity = follower.Tick(Position, speed, deltaTime);
        Body.linearVelocity = velocity;
        currentMove = speed > 0f ? velocity / speed : Vector2.zero;
        if (!aiming && currentMove.sqrMagnitude > MovingThreshold)
        {
            facing = currentMove.normalized;
        }
    }

    /// <summary>
    /// For a destination with no walkable cell near it (e.g. far outside the map): walks back along the line toward
    /// the body until a walkable cell, and paths there instead.
    /// </summary>
    private bool BuildTowardWalkableGround(INavigation2D navigation, Vector2 destination)
    {
        Vector2 start = Position;
        Vector2 toStart = start - destination;
        float distance = toStart.magnitude;
        if (distance <= GroundSearchStep)
        {
            return false;
        }

        Vector2 step = toStart / distance * GroundSearchStep;
        Vector2 point = destination;
        for (float travelled = GroundSearchStep; travelled < distance; travelled += GroundSearchStep)
        {
            point += step;
            Vector3Int cell = navigation.WorldToCell(point);
            if (navigation.IsCellWalkable(cell, settings.MovementProfile))
            {
                return follower.BuildPath(start, navigation.CellToWorldCenter(cell), allowPartial: true);
            }
        }

        return false;
    }

    /// <summary>
    /// Sets the animator's moving flag and move and last-move directions, and plays at the walk animation speed while
    /// moving.
    /// </summary>
    private void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        bool isMoving = currentMove.sqrMagnitude > MovingThreshold;
        Vector2 animationDirection = isMoving ? currentMove.normalized : facing;
        animator.SetBool(IsMovingHash, isMoving);
        animator.SetFloat(MoveXHash, animationDirection.x);
        animator.SetFloat(MoveYHash, animationDirection.y);
        animator.SetFloat(LastMoveXHash, facing.x);
        animator.SetFloat(LastMoveYHash, facing.y);
        animator.speed = isMoving ? walkAnimationSpeed : 1f;
    }

    /// <summary>
    /// Falls back to the first <see cref="Animator"/> in the children, inactive ones included, when none is assigned.
    /// </summary>
    private void ResolveAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }
}
