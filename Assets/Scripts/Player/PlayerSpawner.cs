using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public sealed class PlayerSpawner
{
    private readonly IObjectResolver resolver;
    private readonly PlayerRegistry players;
    private readonly PlayerController playerPrefab;
    private readonly Scene scene;

    public PlayerSpawner(IObjectResolver resolver, PlayerRegistry players, PlayerController playerPrefab, Scene scene)
    {
        this.resolver = resolver;
        this.players = players;
        this.playerPrefab = playerPrefab;
        this.scene = scene;
    }

    public LocalPlayer SpawnLocalPlayer()
    {
        PlayerController controller = resolver.Instantiate(playerPrefab);
        controller.name = playerPrefab.name;
        SceneManager.MoveGameObjectToScene(controller.gameObject, scene);

        LocalPlayer localPlayer = new(controller);
        players.Add(localPlayer.Handle);
        return localPlayer;
    }
}
