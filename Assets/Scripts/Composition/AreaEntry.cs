using UnityEngine;

public sealed class AreaEntry : IAreaEntry
{
    private readonly AreaEntryRequest request;
    private readonly SpawnPoint[] spawnPoints;
    private readonly LocalPlayer player;
    private readonly CameraFollow2D cameraFollow;

    public AreaEntry(
        AreaEntryRequest request,
        SpawnPoint[] spawnPoints,
        LocalPlayer player,
        CameraFollow2D cameraFollow)
    {
        this.request = request;
        this.spawnPoints = spawnPoints;
        this.player = player;
        this.cameraFollow = cameraFollow;
    }

    public void Enter()
    {
        SpawnPoint spawnPoint = FindSpawnPoint(request.SpawnId);
        if (spawnPoint == null)
        {
            Debug.LogWarning($"Area has no SpawnPoint; the player stays at {player.Transform.position}.");
            return;
        }

        player.Controller.Teleport(spawnPoint.transform.position);
        cameraFollow.SnapToTarget();
    }

    private SpawnPoint FindSpawnPoint(string spawnId)
    {
        SpawnPoint fallback = null;
        foreach (SpawnPoint candidate in spawnPoints)
        {
            if (candidate == null)
            {
                continue;
            }

            if (candidate.SpawnId == spawnId)
            {
                return candidate;
            }

            fallback ??= candidate;
        }

        if (fallback != null)
        {
            Debug.LogWarning($"No SpawnPoint with id '{spawnId}'; using '{fallback.SpawnId}' instead.", fallback);
        }

        return fallback;
    }
}
