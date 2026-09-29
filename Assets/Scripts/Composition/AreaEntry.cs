using UnityEngine;

public sealed class AreaEntry : IAreaEntry
{
    private readonly SpawnPoint[] spawnPoints;
    private readonly PlayerController player;
    private readonly CameraFollow2D cameraFollow;

    public AreaEntry(SpawnPoint[] spawnPoints, PlayerController player, CameraFollow2D cameraFollow)
    {
        this.spawnPoints = spawnPoints;
        this.player = player;
        this.cameraFollow = cameraFollow;
    }

    public void Enter(string spawnId)
    {
        SpawnPoint spawnPoint = FindSpawnPoint(spawnId);
        if (spawnPoint == null)
        {
            Debug.LogWarning($"Area has no SpawnPoint; the player stays at {player.transform.position}.");
            return;
        }

        player.Teleport(spawnPoint.transform.position);
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
