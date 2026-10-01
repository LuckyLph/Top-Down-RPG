using Unity.Netcode;
using UnityEngine.SceneManagement;
using VContainer;

public interface INetworkObjectSpawner
{
    bool IsActive { get; }
    bool IsServer { get; }

    void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver, Scene targetScene);
    void UnregisterPrefab(NetworkObject prefab);
    void Spawn(NetworkObject instance);
}
