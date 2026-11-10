using UnityEngine;

/// <summary>
/// Hooks a spawned player into the Gameplay scope: the local player gets the local command source, simulates its own
/// movement, draws the look the local settings choose (<see cref="PlayerLook.FollowLocalChoice"/>), offline and online
/// alike, joins the registry and becomes the tracked local player; a remote copy only joins the registry and shows replicated movement. Placing a player is
/// the world session's (its start, trips and respawns).
/// </summary>
public sealed class PlayerBinder
{
    private readonly PlayerRegistry players;
    private readonly LocalPlayerTracker localPlayer;
    private readonly LocalPlayerCommandSource localCommands;

    /// <summary>
    /// Captures the registry, the local player tracker and the local command source.
    /// </summary>
    public PlayerBinder(PlayerRegistry players, LocalPlayerTracker localPlayer, LocalPlayerCommandSource localCommands)
    {
        this.players = players;
        this.localPlayer = localPlayer;
        this.localCommands = localCommands;
    }

    /// <summary>
    /// Binds <paramref name="controller"/> as this machine's player: drives it from local input, has it follow the
    /// local settings' look, registers it and publishes it as the local player. Returns its registry handle.
    /// </summary>
    public PlayerHandle BindLocal(PlayerController controller)
    {
        controller.SetCommandSource(localCommands);
        controller.SetSimulatesMovement(true);
        if (controller.TryGetComponent(out PlayerLook look))
        {
            look.FollowLocalChoice();
        }

        LocalPlayer local = new(controller);
        players.Add(local.Handle);
        localPlayer.Assign(local);
        return local.Handle;
    }

    /// <summary>
    /// Binds <paramref name="controller"/> as another machine's player: no command source, no local movement
    /// simulation, registered so mobs and the death handling see it. Returns its registry handle.
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
