public sealed class PlayerBinder
{
    private readonly PlayerRegistry players;
    private readonly LocalPlayerTracker localPlayer;
    private readonly LocalPlayerCommandSource localCommands;
    private readonly ActiveSpawnPoint activeSpawnPoint;

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

    public PlayerHandle BindLocal(PlayerController controller)
    {
        controller.SetCommandSource(localCommands);
        controller.SetSimulatesMovement(true);
        LocalPlayer local = new(controller);
        players.Add(local.Handle);

        SpawnPoint spawnPoint = activeSpawnPoint.Current;
        if (spawnPoint != null)
        {
            controller.Teleport(spawnPoint.GetSlotPosition(players.Players.Count - 1));
        }

        localPlayer.Assign(local);
        return local.Handle;
    }

    public PlayerHandle BindRemote(PlayerController controller, IPlayerCommandSource commands)
    {
        controller.SetCommandSource(commands);
        controller.SetSimulatesMovement(false);
        PlayerHandle handle = new(controller.transform, controller.GetComponent<Health>());
        players.Add(handle);
        return handle;
    }

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
