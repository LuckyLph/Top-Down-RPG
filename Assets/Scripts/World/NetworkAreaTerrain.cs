using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a chunk's runtime terrain edits. The host spawns one per chunk of a seamless world
/// (<see cref="ChunkTerrainSync"/>) and keeps a server-only list in step with <see cref="AreaTerrain.Edits"/>. A client
/// applies the whole list when the object reaches it, which happens once it holds the chunk, so a client arriving in
/// the chunk catches up as a late joiner does, then each edit as it changes.
/// </summary>
[DisallowMultipleComponent]
public class NetworkAreaTerrain : NetworkBehaviour
{
    private readonly NetworkList<TerrainEdit> edits = new(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly List<TerrainEdit> pending = new();

    private AreaTerrain terrain;

    internal int EditCount => edits.Count;

    /// <summary>
    /// Receives the area's terrain, which the edits are read from on the host and applied to on clients.
    /// </summary>
    [Inject]
    public void Construct(AreaTerrain areaTerrain)
    {
        terrain = areaTerrain;
    }

    /// <summary>
    /// The replicated edit at <paramref name="index"/>, for tests.
    /// </summary>
    internal TerrainEdit GetEdit(int index)
    {
        return edits[index];
    }

    /// <summary>
    /// On the host, copies every edit recorded so far into the replicated list and follows new ones; on a client,
    /// applies the whole list at once and then follows its changes. Logs an error when no terrain was injected.
    /// </summary>
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

    /// <summary>
    /// Stops following the terrain's recorded edits and the replicated list's changes.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        if (terrain != null)
        {
            terrain.EditRecorded -= WriteEdit;
        }

        edits.OnListChanged -= HandleListChanged;
    }

    /// <summary>
    /// Host only: mirrors the terrain edit at <paramref name="index"/> into the replicated list, replacing or
    /// appending it.
    /// </summary>
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

    /// <summary>
    /// Client only: applies an added, inserted or replaced edit to the terrain; other list changes are ignored.
    /// </summary>
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
