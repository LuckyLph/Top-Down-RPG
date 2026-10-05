/// <summary>
/// Distances and options a <see cref="PathFollower2D"/> builds and follows paths with.
/// </summary>
public readonly struct PathFollowerSettings2D
{
    /// <summary>
    /// Creates settings; a non-positive <paramref name="nearestCellSearchRadius"/> uses the grid's own radius, and the
    /// straight-line shortcut is off unless asked for.
    /// </summary>
    public PathFollowerSettings2D(
        float waypointReachDistance,
        float arrivalDistance,
        int nearestCellSearchRadius = -1,
        bool useStraightLineShortcut = false)
    {
        WaypointReachDistance = waypointReachDistance;
        ArrivalDistance = arrivalDistance;
        NearestCellSearchRadius = nearestCellSearchRadius;
        UseStraightLineShortcut = useStraightLineShortcut;
    }

    public float WaypointReachDistance { get; }
    public float ArrivalDistance { get; }
    public int NearestCellSearchRadius { get; }
    public bool UseStraightLineShortcut { get; }
}
