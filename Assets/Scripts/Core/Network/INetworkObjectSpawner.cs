using Unity.Netcode;
using VContainer;

public interface INetworkObjectSpawner
{
    bool IsActive { get; }
    bool IsServer { get; }

    void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver);
    void UnregisterPrefab(NetworkObject prefab);
    void Spawn(NetworkObject instance);
}
