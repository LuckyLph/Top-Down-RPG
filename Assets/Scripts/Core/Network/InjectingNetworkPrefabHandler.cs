using Unity.Netcode;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class InjectingNetworkPrefabHandler : INetworkPrefabInstanceHandler
{
    private readonly NetworkObject prefab;
    private readonly IObjectResolver resolver;

    public InjectingNetworkPrefabHandler(NetworkObject prefab, IObjectResolver resolver)
    {
        this.prefab = prefab;
        this.resolver = resolver;
    }

    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        return resolver.Instantiate(prefab, position, rotation);
    }

    public void Destroy(NetworkObject networkObject)
    {
        if (networkObject != null)
        {
            Object.Destroy(networkObject.gameObject);
        }
    }
}
