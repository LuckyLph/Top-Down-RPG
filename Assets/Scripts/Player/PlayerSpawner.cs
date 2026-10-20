using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Creates player instances from the player prefab through the Gameplay resolver in the players scene. Offline the
/// local player is bound directly; in a session the host spawns each as the owning client's network player object and
/// <see cref="PlayerNetworkSync"/> binds it on every machine.
/// </summary>
public sealed class PlayerSpawner
{
    private readonly IObjectResolver resolver;
    private readonly PlayerBinder binder;
    private readonly NetworkSession session;
    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly GameplaySettings settings;
    private readonly PlayerController playerPrefab;
    private readonly Scene scene;

    /// <summary>
    /// Captures the resolver that injects new players, the binder, the session, the players and their worlds a joining
    /// player is placed among, the entry slot spacing, the player prefab and the scene new players are moved into.
    /// </summary>
    public PlayerSpawner(
        IObjectResolver resolver,
        PlayerBinder binder,
        NetworkSession session,
        IPlayerRegistry players,
        PlayerWorlds playerWorlds,
        GameplaySettings settings,
        PlayerController playerPrefab,
        Scene scene)
    {
        this.resolver = resolver;
        this.binder = binder;
        this.session = session;
        this.players = players;
        this.playerWorlds = playerWorlds;
        this.settings = settings;
        this.playerPrefab = playerPrefab;
        this.scene = scene;
    }

    public NetworkObject PlayerNetworkPrefab => playerPrefab.GetComponent<NetworkObject>();
    public Scene PlayersScene => scene;

    /// <summary>
    /// Creates this machine's player: in a session it is spawned as the local client's network player object,
    /// offline it is bound as the local player straight away.
    /// </summary>
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

    /// <summary>
    /// Host only: creates the network player object owned by client <paramref name="ownerClientId"/> on a free slot at
    /// <paramref name="position"/> (<see cref="EntrySlots"/>, so players joining at the same entry stand apart), puts
    /// it in <paramref name="world"/> as it spawns and spawns it, unless that client already has one. Logs an error
    /// when called anywhere but the host.
    /// </summary>
    public void SpawnRemotePlayer(ulong ownerClientId, WorldDefinition world, Vector2 position)
    {
        if (!session.IsServer)
        {
            Debug.LogError($"Only the host spawns players for other clients (client {ownerClientId}).");
            return;
        }

        if (session.HasPlayerObject(ownerClientId))
        {
            return;
        }

        float spacing = settings != null ? settings.EntrySlotSpacing : 0f;
        Vector2 slot = EntrySlots.FreeSlot(position, spacing, world, players, playerWorlds, null);
        PlayerController controller = CreateInstance();
        controller.Teleport(slot);
        if (controller.TryGetComponent(out PlayerWorldSync worldSync))
        {
            worldSync.SetSpawnWorld(world);
        }

        session.SpawnPlayerObject(controller.GetComponent<NetworkObject>(), ownerClientId);
    }

    /// <summary>
    /// Instantiates the prefab with injection, gives it the prefab's name and moves it into the players scene.
    /// </summary>
    private PlayerController CreateInstance()
    {
        PlayerController controller = resolver.Instantiate(playerPrefab);
        controller.name = playerPrefab.name;
        SceneManager.MoveGameObjectToScene(controller.gameObject, scene);
        return controller;
    }
}
