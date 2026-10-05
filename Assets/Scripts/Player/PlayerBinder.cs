using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hooks a spawned player into the Gameplay scope: the local player gets the local command source, simulates its own
/// movement, joins the registry, is placed at its slot of the active spawn point and becomes the tracked local player;
/// a remote copy only joins the registry and shows replicated movement.
/// </summary>
public sealed class PlayerBinder
{
    private readonly PlayerRegistry players;
    private readonly LocalPlayerTracker localPlayer;
    private readonly LocalPlayerCommandSource localCommands;
    private readonly ActiveSpawnPoint activeSpawnPoint;

    /// <summary>
    /// Captures the registry, the local player tracker, the local command source and the active spawn point.
    /// </summary>
    public PlayerBinder(
        PlayerRegistry players,
        LocalPlayerTracker localPlayer,
        LocalPlayerCommandSource localCommands,
        ActiveSpawnPoint activeSpawnPoint)
    {
        this.players = players;
        this.localPlayer = localPlayer;
        this.localCommands = localCommands;
        this.activeSpawnPoint = activeSpawnPoint;
    }

    /// <summary>
    /// Binds <paramref name="controller"/> as this machine's player: drives it from local input, registers it, moves
    /// it to its spawn slot and publishes it as the local player. Returns its registry handle.
    /// </summary>
    public PlayerHandle BindLocal(PlayerController controller)
    {
        controller.SetCommandSource(localCommands);
        controller.SetSimulatesMovement(true);
        LocalPlayer local = new(controller);
        players.Add(local.Handle);
        PlaceAtActiveSpawn(local.Handle);
        localPlayer.Assign(local);
        return local.Handle;
    }

    /// <summary>
    /// Teleports the player behind <paramref name="handle"/> to the active spawn point's slot matching its registry
    /// index. Does nothing without a spawn point or when the handle is not a registered player.
    /// </summary>
    public void PlaceAtActiveSpawn(PlayerHandle handle)
    {
        SpawnPoint spawnPoint = activeSpawnPoint.Current;
        if (spawnPoint == null || handle == null || !handle.Transform.TryGetComponent(out PlayerController controller))
        {
            return;
        }

        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = 0; i < party.Count; i++)
        {
            if (party[i] == handle)
            {
                controller.Teleport(spawnPoint.GetSlotPosition(i));
                return;
            }
        }
    }

    /// <summary>
    /// Binds <paramref name="controller"/> as another machine's player: no command source, no local movement
    /// simulation, registered so mobs and the party logic see it. Returns its registry handle.
    /// </summary>
    public PlayerHandle BindRemote(PlayerController controller)
    {
        controller.SetCommandSource(null);
        controller.SetSimulatesMovement(false);
        PlayerHandle handle = new(controller.transform, controller.GetComponent<Health>());
        players.Add(handle);
        return handle;
    }

    /// <summary>
    /// Removes the player behind <paramref name="handle"/> from the registry and clears it as the local player if it
    /// was; null is ignored.
    /// </summary>
    public void Unbind(PlayerHandle handle)
    {
        if (handle == null)
        {
            return;
        }

        localPlayer.Clear(handle);
        players.Remove(handle);
    }
}
