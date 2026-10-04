using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates an area's runtime terrain edits. The host spawns one per area (<see cref="AreaTerrainSync"/>) and keeps a
/// server-only list in step with <see cref="AreaTerrain.Edits"/>. A client applies the whole list when the object
/// reaches it, which happens once it is ready for the area, so a late joiner catches up, then each edit as it changes.
/// </summary>
[DisallowMultipleComponent]
public class NetworkAreaTerrain : NetworkBehaviour
{
    private readonly NetworkList<TerrainEdit> edits = new(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly List<TerrainEdit> pending = new();

    private AreaTerrain terrain;

    internal int EditCount => edits.Count;

    [Inject]
    public void Construct(AreaTerrain areaTerrain)
    {
        terrain = areaTerrain;
    }

    internal TerrainEdit GetEdit(int index)
    {
        return edits[index];
    }

    public override void OnNetworkSpawn()
    {
        if (terrain == null)
        {
            Debug.LogError($"{name} was spawned without an {nameof(AreaTerrain)}; terrain edits will not replicate.", this);
            return;
        }

        if (IsServer)
        {
            edits.Clear();
            for (int i = 0; i < terrain.Edits.Count; i++)
            {
                edits.Add(terrain.Edits[i]);
            }

            terrain.EditRecorded += WriteEdit;
            return;
        }

        edits.OnListChanged += HandleListChanged;
        pending.Clear();
        for (int i = 0; i < edits.Count; i++)
        {
            pending.Add(edits[i]);
        }

        terrain.ApplyReplicated(pending);
    }

    public override void OnNetworkDespawn()
    {
        if (terrain != null)
        {
            terrain.EditRecorded -= WriteEdit;
        }

        edits.OnListChanged -= HandleListChanged;
    }

    private void WriteEdit(int index)
    {
        if (index < edits.Count)
        {
            edits[index] = terrain.Edits[index];
        }
        else
        {
            edits.Add(terrain.Edits[index]);
        }
    }

    private void HandleListChanged(NetworkListEvent<TerrainEdit> change)
    {
        if (change.Type != NetworkListEvent<TerrainEdit>.EventType.Add
            && change.Type != NetworkListEvent<TerrainEdit>.EventType.Insert
            && change.Type != NetworkListEvent<TerrainEdit>.EventType.Value)
        {
            return;
        }

        pending.Clear();
        pending.Add(change.Value);
        terrain.ApplyReplicated(pending);
    }
}
