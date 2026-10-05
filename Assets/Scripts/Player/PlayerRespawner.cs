using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Brings a dead registered player back: restores its health and, when this machine moves that player, places it at
/// its slot of the active spawn point.
/// </summary>
public sealed class PlayerRespawner
{
    private readonly IPlayerRegistry players;
    private readonly ActiveSpawnPoint activeSpawnPoint;

    /// <summary>
    /// Captures the registry that gives each player's spawn slot and the active spawn point.
    /// </summary>
    public PlayerRespawner(IPlayerRegistry players, ActiveSpawnPoint activeSpawnPoint)
    {
        this.players = players;
        this.activeSpawnPoint = activeSpawnPoint;
    }

    /// <summary>
    /// Restores <paramref name="player"/>'s health and teleports it to its spawn slot when this machine simulates its
    /// movement (a remote owner moves itself on the restore). False when the player is not a dead registered one.
    /// </summary>
    public bool TryRespawn(PlayerHandle player)
    {
        if (player == null || player.Transform == null || player.Health == null || !player.Health.IsDead || !players.Contains(player))
        {
            return false;
        }

        player.Health.Restore();

        SpawnPoint spawnPoint = activeSpawnPoint.Current;
        if (spawnPoint != null && player.Transform.TryGetComponent(out PlayerController controller) && controller.SimulatesMovement)
        {
            controller.Teleport(spawnPoint.GetSlotPosition(IndexOf(player)));
        }

        return true;
    }

    /// <summary>
    /// <paramref name="player"/>'s index in the registry, which is its spawn slot; 0 when it is not found.
    /// </summary>
    private int IndexOf(PlayerHandle player)
    {
        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = 0; i < party.Count; i++)
        {
            if (party[i] == player)
            {
                return i;
            }
        }

        return 0;
    }
}
