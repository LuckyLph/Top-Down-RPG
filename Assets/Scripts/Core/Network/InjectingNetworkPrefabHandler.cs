using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public sealed class InjectingNetworkPrefabHandler : INetworkPrefabInstanceHandler
{
    private readonly NetworkObject prefab;
    private readonly IObjectResolver resolver;
    private readonly Scene targetScene;

    public InjectingNetworkPrefabHandler(NetworkObject prefab, IObjectResolver resolver, Scene targetScene = default)
    {
        this.prefab = prefab;
        this.resolver = resolver;
        this.targetScene = targetScene;
    }

    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        NetworkObject instance = resolver.Instantiate(prefab, position, rotation);
        if (targetScene.IsValid() && targetScene.isLoaded && instance.gameObject.scene != targetScene)
        {
            SceneManager.MoveGameObjectToScene(instance.gameObject, targetScene);
        }

        return instance;
    }

    public void Destroy(NetworkObject networkObject)
    {
        if (networkObject != null)
        {
            Object.Destroy(networkObject.gameObject);
        }
    }
}
