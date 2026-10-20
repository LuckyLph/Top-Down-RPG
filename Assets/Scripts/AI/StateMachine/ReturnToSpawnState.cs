using UnityEngine;

/// <summary>
/// Mob state that walks back to its spawn cell (or the nearest walkable cell to it), then idles; chases a detected
/// target it can reach on the way.
/// </summary>
public class ReturnToSpawnState : MobStateBase
{
    private Vector2 returnDestination;
    private Vector3Int returnCell;
    private bool hasReturnDestination;

    /// <summary>
    /// Creates the return state for the mob run by <paramref name="brain"/>.
    /// </summary>
    public ReturnToSpawnState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Return;

    /// <summary>
    /// Clears the path, resolves the return destination and its cell, and paths there; goes to Idle when there is no
    /// destination or path.
    /// </summary>
    public override void Enter()
    {
        PathAgent.ClearPath();

        hasReturnDestination = TryResolveReturnDestination(out returnDestination);
        if (!hasReturnDestination)
        {
            Brain.ChangeState(MobStateId.Idle);
            return;
        }

        if (PathAgent.Navigation != null)
        {
            returnCell = PathAgent.Navigation.WorldToCell(returnDestination);
        }

        bool pathFound = PathAgent.BuildPathToWorld(returnDestination, allowPartial: true);
        if (!pathFound)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    /// <summary>
    /// Goes to Chase when it spots a target within its leash that it can reach; otherwise goes to Idle once in the
    /// return cell, arrived, or stalled for <see cref="MobConfig.StuckTimeout"/>.
    /// </summary>
    public override void Tick()
    {
        if (SpotsTarget())
        {
            Brain.ChangeState(MobStateId.Chase);
            return;
        }

        if (!hasReturnDestination)
        {
            Brain.ChangeState(MobStateId.Idle);
            return;
        }

        if (IsInReturnCell())
        {
            Brain.ChangeState(MobStateId.Idle);
            return;
        }

        if (PathAgent.ReachedDestination || PathAgent.StalledTime >= Config.StuckTimeout)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    /// <summary>
    /// Follows the path.
    /// </summary>
    public override void FixedTick()
    {
        PathAgent.FixedTick();
    }

    /// <summary>
    /// The center of the spawn cell, or of the walkable cell nearest to it; the raw spawn position without a grid.
    /// False when no walkable cell lies near the spawn.
    /// </summary>
    private bool TryResolveReturnDestination(out Vector2 destination)
    {
        destination = Patrol.SpawnPosition;
        if (PathAgent.Navigation == null)
        {
            return true;
        }

        TerrainMovementProfile2D movementProfile = Config != null ? Config.MovementProfile : null;
        Vector3Int spawnCell = PathAgent.Navigation.WorldToCell(Patrol.SpawnPosition);
        if (PathAgent.Navigation.IsCellWalkable(spawnCell, movementProfile))
        {
            destination = PathAgent.Navigation.CellToWorldCenter(spawnCell);
            return true;
        }

        if (PathAgent.Navigation.TryGetNearestWalkableCell(spawnCell, movementProfile, out Vector3Int nearest, Config.NearestCellSearchRadius))
        {
            destination = PathAgent.Navigation.CellToWorldCenter(nearest);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the mob stands in the return destination's cell; false without a grid.
    /// </summary>
    private bool IsInReturnCell()
    {
        if (PathAgent.Navigation == null)
        {
            return false;
        }

        Vector3Int currentCell = PathAgent.Navigation.WorldToCell(Transform.position);
        return currentCell == returnCell;
    }
}
