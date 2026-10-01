using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public sealed class PlayerSpawner
{
    private readonly IObjectResolver resolver;
    private readonly PlayerRegistry players;
    private readonly LocalPlayerCommandSource localCommands;
    private readonly PlayerController playerPrefab;
    private readonly Scene scene;

    public PlayerSpawner(
        IObjectResolver resolver,
        PlayerRegistry players,
        LocalPlayerCommandSource localCommands,
        PlayerController playerPrefab,
        Scene scene)
    {
        this.resolver = resolver;
        this.players = players;
        this.localCommands = localCommands;
        this.playerPrefab = playerPrefab;
        this.scene = scene;
    }

    public LocalPlayer SpawnLocalPlayer()
    {
        PlayerController controller = resolver.Instantiate(playerPrefab);
        controller.name = playerPrefab.name;
        controller.SetCommandSource(localCommands);
        SceneManager.MoveGameObjectToScene(controller.gameObject, scene);

        LocalPlayer localPlayer = new(controller);
        players.Add(localPlayer.Handle);
        return localPlayer;
    }
}
