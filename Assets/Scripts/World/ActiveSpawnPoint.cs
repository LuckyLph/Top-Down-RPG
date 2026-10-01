public sealed class ActiveSpawnPoint
{
    public SpawnPoint Current { get; private set; }

    public void Set(SpawnPoint spawnPoint)
    {
        Current = spawnPoint;
    }
}
