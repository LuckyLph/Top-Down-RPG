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

    public NavigationGrid2D NavigationGrid => follower.NavigationGrid;
    public bool HasPath => follower.HasPath;
    public bool ReachedDestination => follower.ReachedDestination;
    public bool IsPartialPath => follower.IsPartialPath;
    public bool ReachedResolvedGoal => follower.ReachedResolvedGoal;
    public bool GoalWasAdjusted => follower.GoalWasAdjusted;
    public bool HasGoalCell => follower.HasGoalCell;
    public Vector3Int LastGoalCell => follower.LastGoalCell;
    public float StalledTime => follower.StalledTime;

    internal IReadOnlyList<Vector2> Waypoints => follower.Waypoints;

    private void Awake()
    {
        motor = GetComponent<MobMotor2D>();
    }

    public void Initialize(NavigationGrid2D navGrid, MobMotor2D mobMotor, MobConfig mobConfig)
    {
        motor = mobMotor;
        config = mobConfig;
        follower.Configure(navGrid, config != null ? config.MovementProfile : null, CreateSettings(config));
        ClearPath();
    }

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

    public bool CanReachWorldTarget(Vector2 worldGoal)
    {
        return motor != null && config != null && follower.CanReach(motor.Position, worldGoal);
    }

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

    public void ClearPath()
    {
        follower.Clear();

        if (motor != null)
        {
            motor.Stop();
        }
    }

    private static PathFollowerSettings2D CreateSettings(MobConfig mobConfig)
    {
        return mobConfig != null
            ? new PathFollowerSettings2D(mobConfig.waypointReachDistance, mobConfig.arrivalDistance, mobConfig.nearestCellSearchRadius)
            : default;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos)
        {
            return;
        }

        NavigationGrid2D navigationGrid = follower.NavigationGrid;
        if (follower.HasGoalCell && navigationGrid != null)
        {
            Gizmos.color = goalColor;
            Gizmos.DrawWireSphere(navigationGrid.CellToWorldCenter(follower.LastGoalCell), 0.08f);
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
