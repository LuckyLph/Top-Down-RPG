using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Mob wrapper around <see cref="PathFollower2D"/>: builds paths from the mob's position with its
/// <see cref="MobConfig"/> navigation tuning and feeds the follower's velocity to the <see cref="MobMotor2D"/>.
/// </summary>
public class MobPathAgent2D : MonoBehaviour
{
    private static readonly ProfilerMarker BuildPathMarker = new("MobPathAgent2D.BuildPathToWorld");

    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private Color pathColor = new(1f, 1f, 0f, 0.9f);
    [SerializeField] private Color partialPathColor = new(1f, 0.5f, 0.2f, 0.9f);
    [SerializeField] private Color goalColor = new(0.2f, 1f, 0.2f, 0.9f);

    private readonly PathFollower2D follower = new();
    private MobConfig config;
    private MobMotor2D motor;

    public INavigation2D Navigation => follower.Navigation;
    public bool HasPath => follower.HasPath;
    public bool ReachedDestination => follower.ReachedDestination;
    public bool IsPartialPath => follower.IsPartialPath;
    public bool ReachedResolvedGoal => follower.ReachedResolvedGoal;
    public bool GoalWasAdjusted => follower.GoalWasAdjusted;
    public bool HasGoalCell => follower.HasGoalCell;
    public Vector3Int LastGoalCell => follower.LastGoalCell;
    public float StalledTime => follower.StalledTime;

    internal IReadOnlyList<Vector2> Waypoints => follower.Waypoints;

    /// <summary>
    /// Caches the sibling motor until <see cref="Initialize"/> provides one.
    /// </summary>
    private void Awake()
    {
        motor = GetComponent<MobMotor2D>();
    }

    /// <summary>
    /// Configures the follower with <paramref name="navigation"/> and the config's movement profile and distances (no
    /// straight-line shortcut), and clears any path.
    /// </summary>
    public void Initialize(INavigation2D navigation, MobMotor2D mobMotor, MobConfig mobConfig)
    {
        motor = mobMotor;
        config = mobConfig;
        follower.Configure(navigation, config != null ? config.MovementProfile : null, CreateSettings(config));
        ClearPath();
    }

    /// <summary>
    /// Builds a path from the motor's position to <paramref name="worldGoal"/>, stopping the motor when no path
    /// results. False when the agent has no config or built grid, or no path exists.
    /// </summary>
    public bool BuildPathToWorld(Vector2 worldGoal, bool allowPartial)
    {
        using (BuildPathMarker.Auto())
        {
            if (motor == null || config == null || !follower.IsConfigured)
            {
                return false;
            }

            bool built = follower.BuildPath(motor.Position, worldGoal, allowPartial);
            if (!follower.HasPath)
            {
                motor.Stop();
            }

            return built;
        }
    }

    /// <summary>
    /// Whether <paramref name="worldGoal"/> lies on a walkable cell in the same region as the walkable cell nearest the
    /// mob; false before the agent is initialized.
    /// </summary>
    public bool CanReachWorldTarget(Vector2 worldGoal)
    {
        return motor != null && config != null && follower.CanReach(motor.Position, worldGoal);
    }

    /// <summary>
    /// Advances along the path at the motor's current speed and hands the velocity to the motor, stopping it when
    /// there is no path or the path just ended.
    /// </summary>
    public void FixedTick()
    {
        if (motor == null)
        {
            return;
        }

        if (!follower.HasPath || config == null)
        {
            motor.Stop();
            return;
        }

        Vector2 desiredVelocity = follower.Tick(motor.Position, motor.CurrentMoveSpeed, Time.fixedDeltaTime);
        if (follower.HasPath)
        {
            motor.SetDesiredVelocity(desiredVelocity);
        }
        else
        {
            motor.Stop();
        }
    }

    /// <summary>
    /// Drops the path and its goal and stops the motor.
    /// </summary>
    public void ClearPath()
    {
        follower.Clear();

        if (motor != null)
        {
            motor.Stop();
        }
    }

    /// <summary>
    /// Follower settings from the config's waypoint reach and arrival distances, searching for the nearest walkable cell
    /// as far as the navigation's own radius; the defaults without a config.
    /// </summary>
    private static PathFollowerSettings2D CreateSettings(MobConfig mobConfig)
    {
        return mobConfig != null
            ? new PathFollowerSettings2D(mobConfig.WaypointReachDistance, mobConfig.ArrivalDistance)
            : default;
    }

    /// <summary>
    /// Draws the goal cell and the remaining waypoints, in a different color for a partial path.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos)
        {
            return;
        }

        INavigation2D navigation = follower.Navigation;
        if (follower.HasGoalCell && navigation != null)
        {
            Gizmos.color = goalColor;
            Gizmos.DrawWireSphere(navigation.CellToWorldCenter(follower.LastGoalCell), 0.08f);
        }

        IReadOnlyList<Vector2> waypoints = follower.Waypoints;
        if (!follower.HasPath || waypoints.Count == 0)
        {
            return;
        }

        Gizmos.color = follower.IsPartialPath ? partialPathColor : pathColor;
        Vector3 previous = transform.position;
        int startIndex = Mathf.Clamp(follower.NextWaypointIndex, 0, waypoints.Count - 1);
        for (int i = startIndex; i < waypoints.Count; i++)
        {
            Vector3 waypoint = waypoints[i];
            Gizmos.DrawLine(previous, waypoint);
            Gizmos.DrawWireSphere(waypoint, 0.06f);
            previous = waypoint;
        }
    }
}
