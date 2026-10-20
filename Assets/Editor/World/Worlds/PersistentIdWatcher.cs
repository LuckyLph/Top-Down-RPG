using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps <see cref="PersistentId"/>s in chunk scenes assigned and unique as objects are made: outside play mode, when a
/// hierarchy is created (a new object, a duplicate, a paste, a prefab instance) or an object's components change in a
/// scene of a world's chunk, every <see cref="MobSpawnPoint"/> in it gets a <see cref="PersistentId"/>, and every id in
/// it that is missing or shared with another loaded object gets a new one (<see cref="PersistentIds.RepairNew"/>), so
/// the duplicate changes and its original keeps its id. Objects changed only by scripts that record no undo raise no
/// event; the World Editor's Repair ids step covers them.
/// </summary>
[InitializeOnLoad]
public static class PersistentIdWatcher
{
    private static bool repairing;

    /// <summary>
    /// Follows the editor's object change events.
    /// </summary>
    static PersistentIdWatcher()
    {
        ObjectChangeEvents.changesPublished += HandleChanges;
    }

    /// <summary>
    /// Repairs the ids of the hierarchies created and the objects whose components changed in chunk scenes.
    /// </summary>
    private static void HandleChanges(ref ObjectChangeEventStream stream)
    {
        if (repairing || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        List<GameObject> touched = null;
        for (int i = 0; i < stream.length; i++)
        {
            GameObject changed = null;
            switch (stream.GetEventType(i))
            {
                case ObjectChangeKind.CreateGameObjectHierarchy:
                    stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs created);
                    changed = EditorUtility.EntityIdToObject(created.entityId) as GameObject;
                    break;
                case ObjectChangeKind.ChangeGameObjectStructure:
                    stream.GetChangeGameObjectStructureEvent(i, out ChangeGameObjectStructureEventArgs structure);
                    changed = EditorUtility.EntityIdToObject(structure.entityId) as GameObject;
                    break;
            }

            if (changed != null && PersistentIds.IsChunkScene(changed.scene))
            {
                touched ??= new List<GameObject>();
                touched.Add(changed);
            }
        }

        if (touched != null)
        {
            Repair(touched);
        }
    }

    /// <summary>
    /// Gives the spawn points under <paramref name="roots"/> their ids and makes those ids unique among every loaded
    /// one.
    /// </summary>
    private static void Repair(List<GameObject> roots)
    {
        repairing = true;
        try
        {
            PersistentIds.AddToSpawnPoints(roots);
            List<PersistentId> fresh = new();
            foreach (GameObject root in roots)
            {
                fresh.AddRange(root.GetComponentsInChildren<PersistentId>(true));
            }

            if (fresh.Count == 0)
            {
                return;
            }

            PersistentId[] loaded = Object.FindObjectsByType<PersistentId>(FindObjectsInactive.Include);
            foreach (PersistentId changed in PersistentIds.RepairNew(fresh, loaded))
            {
                Debug.Log($"{PersistentIds.PathOf(changed)}: new {nameof(PersistentId)} {changed.Id}.", changed);
            }
        }
        finally
        {
            repairing = false;
        }
    }
}
