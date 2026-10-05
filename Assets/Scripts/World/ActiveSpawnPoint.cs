/// <summary>
/// Gameplay-scope holder for the <see cref="SpawnPoint"/> the party entered the current area at, so players who
/// join or respawn later are placed there. Set by <see cref="AreaEntry"/>.
/// </summary>
public sealed class ActiveSpawnPoint
{
    public SpawnPoint Current { get; private set; }

    /// <summary>
    /// Makes <paramref name="spawnPoint"/> the current one; null when the area has no spawn point.
    /// </summary>
    public void Set(SpawnPoint spawnPoint)
    {
        Current = spawnPoint;
    }
}
