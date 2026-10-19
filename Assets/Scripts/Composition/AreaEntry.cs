using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The area scope's <see cref="IAreaEntry"/>: makes this area's navigation grid the one players path on
/// (<see cref="PlayerNavigation"/>) and its requested spawn point the active one, moves the players this machine
/// simulates to their party slots at that spawn point, and snaps the camera. Clears the area's navigation again when the
/// area unloads.
/// </summary>
public sealed class AreaEntry : IAreaEntry, IDisposable
{
    private readonly AreaEntryRequest request;
    private readonly SpawnPoint[] spawnPoints;
    private readonly NavigationGrid2D navigationGrid;
    private readonly IPlayerRegistry players;
    private readonly ActiveSpawnPoint activeSpawnPoint;
    private readonly PlayerNavigation playerNavigation;
    private readonly CameraFollow2D cameraFollow;

    /// <summary>
    /// Captures the entry request, the area's spawn points and navigation grid (may be null), and the players'
    /// navigation, the active spawn point holder, the player registry and the camera follow it uses on entry.
    /// </summary>
    public AreaEntry(
        AreaEntryRequest request,
        SpawnPoint[] spawnPoints,
        NavigationGrid2D navigationGrid,
        IPlayerRegistry players,
        ActiveSpawnPoint activeSpawnPoint,
        PlayerNavigation playerNavigation,
        CameraFollow2D cameraFollow)
    {
        this.request = request;
        this.spawnPoints = spawnPoints;
        this.navigationGrid = navigationGrid;
        this.players = players;
        this.activeSpawnPoint = activeSpawnPoint;
        this.playerNavigation = playerNavigation;
        this.cameraFollow = cameraFollow;
    }

    /// <summary>
    /// Makes the area's grid the players' navigation and sets the active spawn point, then teleports each player this machine simulates to the
    /// spawn slot for its index in the party and snaps the camera. Warns and leaves the players in place when the
    /// area has no spawn point.
    /// </summary>
    public void Enter()
    {
        playerNavigation.SetArea(navigationGrid);

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
    /// Runs when the area's scope is disposed as its scene unloads, so the players' navigation never outlives this
    /// area's grid.
    /// </summary>
    public void Dispose()
    {
        playerNavigation.ClearArea(navigationGrid);
    }

    /// <summary>
    /// The spawn point with <paramref name="spawnId"/>; otherwise the first one in the area, with a warning; null
    /// when the area has none.
    /// </summary>
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
