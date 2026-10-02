using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class AreaEntry : IAreaEntry, IDisposable
{
    private readonly AreaEntryRequest request;
    private readonly SpawnPoint[] spawnPoints;
    private readonly NavigationGrid2D navigationGrid;
    private readonly IPlayerRegistry players;
    private readonly ActiveSpawnPoint activeSpawnPoint;
    private readonly ActiveNavigationGrid activeNavigationGrid;
    private readonly CameraFollow2D cameraFollow;

    public AreaEntry(
        AreaEntryRequest request,
        SpawnPoint[] spawnPoints,
        NavigationGrid2D navigationGrid,
        IPlayerRegistry players,
        ActiveSpawnPoint activeSpawnPoint,
        ActiveNavigationGrid activeNavigationGrid,
        CameraFollow2D cameraFollow)
    {
        this.request = request;
        this.spawnPoints = spawnPoints;
        this.navigationGrid = navigationGrid;
        this.players = players;
        this.activeSpawnPoint = activeSpawnPoint;
        this.activeNavigationGrid = activeNavigationGrid;
        this.cameraFollow = cameraFollow;
    }

    public void Enter()
    {
        activeNavigationGrid.Set(navigationGrid);

        SpawnPoint spawnPoint = FindSpawnPoint(request.SpawnId);
        activeSpawnPoint.Set(spawnPoint);
        if (spawnPoint == null)
        {
            Debug.LogWarning("Area has no SpawnPoint; the players stay where they are.");
            return;
        }

        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = 0; i < party.Count; i++)
        {
            Transform playerTransform = party[i].Transform;
            if (playerTransform != null && playerTransform.TryGetComponent(out PlayerController controller) && controller.SimulatesMovement)
            {
                controller.Teleport(spawnPoint.GetSlotPosition(i));
            }
        }

        cameraFollow.SnapToTarget();
    }

    /// <summary>
    /// Runs when the area's scope is disposed as its scene unloads, so the holder never outlives this area's grid.
    /// </summary>
    public void Dispose()
    {
        activeNavigationGrid.Clear(navigationGrid);
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

            if (fallback == null)
            {
                fallback = candidate;
            }
        }

        if (fallback != null)
        {
            Debug.LogWarning($"No SpawnPoint with id '{spawnId}'; using '{fallback.SpawnId}' instead.", fallback);
        }

        return fallback;
    }
}
