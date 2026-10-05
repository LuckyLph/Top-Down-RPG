using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Creates a client's copies of a network prefab through an <see cref="IObjectResolver"/> so their <c>[Inject]</c>
/// methods run, moving them into a target scene when one is given, and destroys them on despawn. Netcode only calls it
/// on clients; the host instantiates through the resolver itself before spawning.
/// </summary>
public sealed class InjectingNetworkPrefabHandler : INetworkPrefabInstanceHandler
{
    private readonly NetworkObject prefab;
    private readonly IObjectResolver resolver;
    private readonly Scene targetScene;

    /// <summary>
    /// Creates a handler that instantiates <paramref name="prefab"/> through <paramref name="resolver"/>; the default
    /// <paramref name="targetScene"/> leaves instances where the resolver puts them.
    /// </summary>
    public InjectingNetworkPrefabHandler(NetworkObject prefab, IObjectResolver resolver, Scene targetScene = default)
    {
        this.prefab = prefab;
        this.resolver = resolver;
        this.targetScene = targetScene;
    }

    /// <summary>
    /// Instantiates an injected copy of the prefab at <paramref name="position"/> and <paramref name="rotation"/>,
    /// moving it into the target scene when that scene is loaded and the copy is elsewhere.
    /// </summary>
    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        NetworkObject instance = resolver.Instantiate(prefab, position, rotation);
        if (targetScene.IsValid() && targetScene.isLoaded && instance.gameObject.scene != targetScene)
        {
            SceneManager.MoveGameObjectToScene(instance.gameObject, targetScene);
        }

        return instance;
    }

    /// <summary>
    /// Destroys the despawned copy's game object; ignores one that is already gone.
    /// </summary>
    public void Destroy(NetworkObject networkObject)
    {
        if (networkObject != null)
        {
            Object.Destroy(networkObject.gameObject);
        }
    }
}
