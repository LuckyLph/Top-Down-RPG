using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public sealed class PlayerSpawner
{
    private readonly IObjectResolver resolver;
    private readonly PlayerBinder binder;
    private readonly NetworkSession session;
    private readonly PlayerController playerPrefab;
    private readonly Scene scene;

    public PlayerSpawner(
        IObjectResolver resolver,
        PlayerBinder binder,
        NetworkSession session,
        PlayerController playerPrefab,
        Scene scene)
    {
        this.resolver = resolver;
        this.binder = binder;
        this.session = session;
        this.playerPrefab = playerPrefab;
        this.scene = scene;
    }

    public NetworkObject PlayerNetworkPrefab => playerPrefab.GetComponent<NetworkObject>();

    public void SpawnLocalPlayer()
    {
        PlayerController controller = CreateInstance();
        if (session.IsActive)
        {
            session.SpawnPlayerObject(controller.GetComponent<NetworkObject>(), session.LocalClientId);
        }
        else
        {
            binder.BindLocal(controller);
        }
    }

    public void SpawnRemotePlayer(ulong ownerClientId)
    {
        if (!session.IsServer)
        {
            Debug.LogError($"Only the host spawns players for other clients (client {ownerClientId}).");
            return;
        }

        PlayerController controller = CreateInstance();
        session.SpawnPlayerObject(controller.GetComponent<NetworkObject>(), ownerClientId);
    }

    private PlayerController CreateInstance()
    {
        PlayerController controller = resolver.Instantiate(playerPrefab);
        controller.name = playerPrefab.name;
        SceneManager.MoveGameObjectToScene(controller.gameObject, scene);
        return controller;
    }
}
