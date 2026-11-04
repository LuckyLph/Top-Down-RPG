using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps <see cref="PersistentId"/>s in chunk scenes assigned and unique as objects are made: outside play mode, when a
/// hierarchy is created (a new object, a duplicate, a paste, a prefab instance) or an object's components change in a
/// scene of a world's chunk, every <see cref="MobSpawnPoint"/> in it gets a <see cref="PersistentId"/>, and every id in
/// it that is missing or shared with another loaded object gets a new one (<see cref="PersistentIds.RepairNew"/>), so
/// the duplicate changes and its original keeps its id. As a chunk scene saves, whatever changed it (scripts that record
/// no undo raise no change event), every spawn point in it gets an id, every id missing gets one, and of the ids it
/// holds twice the later keep theirs no more (<see cref="PersistentIds.MakeUnique"/>), so the scene saved is whole
/// (Docs/KnownGaps.md, Phase 5); an id it shares with another chunk is left to the World Editor's Repair ids and to
/// Validate, since which of the two is the original cannot be told from one scene.
/// </summary>
[InitializeOnLoad]
public static class PersistentIdWatcher
{
    private static bool repairing;

    /// <summary>
    /// Follows the editor's object change events and scene saves.
    /// </summary>
    static PersistentIdWatcher()
    {
        ObjectChangeEvents.changesPublished += HandleChanges;
        EditorSceneManager.sceneSaving += HandleSceneSaving;
    }

    /// <summary>
    /// Repairs the ids of a chunk scene as it saves, outside play mode.
    /// </summary>
    private static void HandleSceneSaving(Scene scene, string path)
    {
        if (repairing || EditorApplication.isPlayingOrWillChangePlaymode || !PersistentIds.IsChunkScene(scene))
        {
            return;
        }

        foreach (PersistentId changed in RepairScene(scene))
        {
            Debug.Log($"{PersistentIds.PathOf(changed)}: new {nameof(PersistentId)} {changed.Id} as {scene.name} saves.", changed);
        }
    }

    /// <summary>
    /// Gives every spawn point of <paramref name="scene"/> an id and makes the scene's ids set and unique within it, the
    /// first of each id keeping it. Returns the ids it changed.
    /// </summary>
    internal static List<PersistentId> RepairScene(Scene scene)
    {
        repairing = true;
        try
        {
            PersistentIds.AddToSpawnPoints(scene.GetRootGameObjects());
            return PersistentIds.MakeUnique(SceneQuery.FindAll<PersistentId>(scene));
        }
        finally
        {
            repairing = false;
        }
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
