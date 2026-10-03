using UnityEngine;
using VContainer;

/// <summary>
/// Moves the player's <see cref="Rigidbody2D"/> along a <see cref="PathFollower2D"/> path on the current area's
/// grid (a straight line when there is none) at the settings' speed times a speed scale (slows; 0 while stunned or
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
    private ActiveNavigationGrid activeNavigationGrid;
    private PlayerControlSettings settings;
    private NavigationGrid2D configuredGrid;
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

    private void Awake()
    {
        ResolveAnimator();
        Rigidbody2D body = Body;
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void OnValidate()
    {
        ResolveAnimator();
    }

    private void OnDisable()
    {
        Stop();
    }

    private void Update()
    {
        UpdateAnimator();
    }

    private void FixedUpdate()
    {
        FixedTick(Time.fixedDeltaTime);
    }

    [Inject]
    public void Construct(ActiveNavigationGrid navigationGrid)
    {
        activeNavigationGrid = navigationGrid;
    }

    public void Initialize(PlayerControlSettings controlSettings)
    {
        settings = controlSettings;
        followerConfigured = false;
        follower.Clear();
    }

    /// <summary>
    /// Builds a path from the body to <paramref name="destination"/> on the active grid (an unwalkable or
    /// unreachable destination ends at the closest walkable cell), or a straight line without a grid.
    /// </summary>
    public bool MoveTo(Vector2 destination)
    {
        if (settings == null || !simulatesMovement)
        {
            return false;
        }

        NavigationGrid2D grid = activeNavigationGrid != null ? activeNavigationGrid.Current : null;
        if (!followerConfigured || !ReferenceEquals(grid, configuredGrid))
        {
            follower.Configure(grid, settings.MovementProfile, settings.FollowerSettings);
            configuredGrid = grid;
            followerConfigured = true;
        }

        if (follower.IsConfigured)
        {
            return follower.BuildPath(Position, destination, allowPartial: true)
                || BuildTowardWalkableGround(grid, destination);
        }

        follower.BuildStraightPath(Position, destination);
        return true;
    }

    public void Stop()
    {
        follower.Clear();
        currentMove = Vector2.zero;
        if (simulatesMovement && Body != null)
        {
            Body.linearVelocity = Vector2.zero;
        }
    }

    public void Face(Vector2 direction)
    {
        if (direction.sqrMagnitude > MovingThreshold)
        {
            facing = direction.normalized;
        }
    }

    /// <summary>
    /// While aiming, the player faces <paramref name="direction"/> and movement no longer turns it.
    /// </summary>
    /// <summary>
    /// Scales the move speed: below 1 slows the player, 0 holds it still while its path and order are kept.
    /// </summary>
    public void SetSpeedScale(float scale)
    {
        speedScale = Mathf.Max(0f, scale);
    }

    public void SetAim(bool aim, Vector2 direction)
    {
        aiming = aim;
        if (aim)
        {
            Face(direction);
        }
    }

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

    public void Teleport(Vector2 position)
    {
        Stop();
        Rigidbody2D body = Body;
        body.position = position;
        body.linearVelocity = Vector2.zero;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

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
    private bool BuildTowardWalkableGround(NavigationGrid2D grid, Vector2 destination)
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
            Vector3Int cell = grid.WorldToCell(point);
            if (grid.IsCellWalkable(cell, settings.MovementProfile))
            {
                return follower.BuildPath(start, grid.CellToWorldCenter(cell), allowPartial: true);
            }
        }

        return false;
    }

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

    private void ResolveAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }
}
