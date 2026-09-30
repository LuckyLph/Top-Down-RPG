using UnityEngine;

public class ReturnToSpawnState : MobStateBase
{
    private Vector2 returnDestination;
    private Vector3Int returnCell;
    private bool hasReturnDestination;

    public ReturnToSpawnState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Return;

    public override void Enter()
    {
        PathAgent.ClearPath();

        hasReturnDestination = TryResolveReturnDestination(out returnDestination);
        if (!hasReturnDestination)
        {
            Brain.ChangeState(MobStateId.Idle);
            return;
        }

        if (PathAgent.NavigationGrid != null)
        {
            returnCell = PathAgent.NavigationGrid.WorldToCell(returnDestination);
        }

        bool pathFound = PathAgent.BuildPathToWorld(returnDestination, allowPartial: true);
        if (!pathFound)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    public override void Tick()
    {
        if (Perception.HasDetectedTarget && Perception.CurrentTarget != null && PathAgent.CanReachWorldTarget(Perception.CurrentTarget.position))
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

        if (PathAgent.ReachedDestination || PathAgent.StalledTime >= Config.stuckTimeout)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    public override void FixedTick()
    {
        PathAgent.FixedTick();
    }

    private bool TryResolveReturnDestination(out Vector2 destination)
    {
        destination = Patrol.SpawnPosition;
        if (PathAgent.NavigationGrid == null)
        {
            return true;
        }

        TerrainMovementProfile2D movementProfile = Config != null ? Config.MovementProfile : null;
        Vector3Int spawnCell = PathAgent.NavigationGrid.WorldToCell(Patrol.SpawnPosition);
        if (PathAgent.NavigationGrid.IsCellWalkable(spawnCell, movementProfile))
        {
            destination = PathAgent.NavigationGrid.CellToWorldCenter(spawnCell);
            return true;
        }

        if (PathAgent.NavigationGrid.TryGetNearestWalkableCell(spawnCell, movementProfile, out Vector3Int nearest, Config.nearestCellSearchRadius))
        {
            destination = PathAgent.NavigationGrid.CellToWorldCenter(nearest);
            return true;
        }

        return false;
    }

    private bool IsInReturnCell()
    {
        if (PathAgent.NavigationGrid == null)
        {
            return false;
        }

        Vector3Int currentCell = PathAgent.NavigationGrid.WorldToCell(Transform.position);
        return currentCell == returnCell;
    }
}
