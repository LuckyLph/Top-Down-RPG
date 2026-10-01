using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerRespawner
{
    private readonly IPlayerRegistry players;
    private readonly ActiveSpawnPoint activeSpawnPoint;

    public PlayerRespawner(IPlayerRegistry players, ActiveSpawnPoint activeSpawnPoint)
    {
        this.players = players;
        this.activeSpawnPoint = activeSpawnPoint;
    }

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
