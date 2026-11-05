using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives placed objects in chunk scenes their <see cref="PersistentId"/> (Docs/WorldStreaming.md: Terms): every
/// <see cref="MobSpawnPoint"/> of a chunk gets one, an object without an id gets a new GUID, and of several objects
/// sharing an id the first keeps it and the others get new ones, since a duplicated object copies its id. Every change
/// is recorded for undo. <see cref="PersistentIdWatcher"/> runs it on objects as they are created or changed in a chunk
/// scene; the World Editor runs it over a whole world.
/// </summary>
public static class PersistentIds
{
    /// <summary>
    /// A new id: a GUID without dashes.
    /// </summary>
    public static string NewId()
    {
        return System.Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Whether <paramref name="scene"/> is a chunk scene of a world: it holds a <see cref="WorldChunk"/> naming one.
    /// </summary>
    public static bool IsChunkScene(Scene scene)
    {
        WorldChunk chunk = scene.IsValid() && scene.isLoaded ? SceneQuery.FindFirst<WorldChunk>(scene) : null;
        return chunk != null && chunk.World != null;
    }

    /// <summary>
    /// Adds a <see cref="PersistentId"/> to every <see cref="MobSpawnPoint"/> in or under <paramref name="roots"/> that
    /// lacks one, and returns the ids added (without ids yet).
    /// </summary>
    public static List<PersistentId> AddToSpawnPoints(IEnumerable<GameObject> roots)
    {
        List<PersistentId> added = new();
        foreach (GameObject root in roots)
        {
            foreach (MobSpawnPoint point in root.GetComponentsInChildren<MobSpawnPoint>(true))
            {
                if (!point.TryGetComponent(out PersistentId _))
                {
                    added.Add(Undo.AddComponent<PersistentId>(point.gameObject));
                }
            }
        }

        return added;
    }

    /// <summary>
    /// Sets <paramref name="persistentId"/>'s id to <paramref name="id"/> through its serialized object, recorded for
    /// undo.
    /// </summary>
    public static void SetId(PersistentId persistentId, string id)
    {
        SerializedObject data = new(persistentId);
        data.FindProperty("id").stringValue = id;
        data.ApplyModifiedProperties();
    }

    /// <summary>
    /// Gives every id of <paramref name="ids"/> without one a new id, and every one sharing an id with an earlier one
    /// in the list a new id, so the list ends up unique. Returns the objects it changed.
    /// </summary>
    public static List<PersistentId> MakeUnique(IReadOnlyList<PersistentId> ids)
    {
        List<PersistentId> changed = new();
        HashSet<string> seen = new();
        foreach (PersistentId id in ids)
        {
            if (id == null)
            {
                continue;
            }

            if (id.HasId && seen.Add(id.Id))
            {
                continue;
            }

            string fresh = NewId();
            SetId(id, fresh);
            seen.Add(fresh);
            changed.Add(id);
        }

        return changed;
    }

    /// <summary>
    /// Gives each of <paramref name="fresh"/> (objects just created or changed) a new id when it has none or shares
    /// one with another object of <paramref name="existing"/> (every id loaded, the fresh ones included), keeping the
    /// other objects' ids, so a duplicate gets a new id and its original keeps its own. Returns the objects it changed.
    /// </summary>
    public static List<PersistentId> RepairNew(IReadOnlyList<PersistentId> fresh, IEnumerable<PersistentId> existing)
    {
        Dictionary<string, int> counts = new();
        foreach (PersistentId id in existing)
        {
            if (id != null && id.HasId)
            {
                counts.TryGetValue(id.Id, out int count);
                counts[id.Id] = count + 1;
            }
        }

        List<PersistentId> changed = new();
        foreach (PersistentId id in fresh)
        {
            if (id == null)
            {
                continue;
            }

            int others = 0;
            if (id.HasId && counts.TryGetValue(id.Id, out int count))
            {
                others = count - 1;
            }

            if (id.HasId && others == 0)
            {
                continue;
            }

            if (id.HasId)
            {
                counts[id.Id] = others;
            }

            string replacement = NewId();
            SetId(id, replacement);
            counts[replacement] = 1;
            changed.Add(id);
        }

        return changed;
    }

    /// <summary>
    /// The groups of <paramref name="ids"/> sharing an id, each with that id; ids not set are left out.
    /// </summary>
    public static List<KeyValuePair<string, List<PersistentId>>> Duplicates(IEnumerable<PersistentId> ids)
    {
        Dictionary<string, List<PersistentId>> byId = new();
        List<string> order = new();
        foreach (PersistentId id in ids)
        {
            if (id == null || !id.HasId)
            {
                continue;
            }

            if (!byId.TryGetValue(id.Id, out List<PersistentId> group))
            {
                byId[id.Id] = group = new List<PersistentId>();
                order.Add(id.Id);
            }

            group.Add(id);
        }

        List<KeyValuePair<string, List<PersistentId>>> duplicates = new();
        foreach (string key in order)
        {
            if (byId[key].Count > 1)
            {
                duplicates.Add(new KeyValuePair<string, List<PersistentId>>(key, byId[key]));
            }
        }

        return duplicates;
    }

    /// <summary>
    /// Every <see cref="PersistentId"/> of every chunk scene of <paramref name="world"/>, in chunk order and then
    /// hierarchy order, opening the scenes that are not open beside the others (added to <paramref name="opened"/>).
    /// </summary>
    public static List<PersistentId> InWorld(WorldDefinition world, List<Scene> opened)
    {
        List<PersistentId> ids = new();
        foreach (Scene scene in WorldAuthoring.OpenChunkScenes(world, opened).Values)
        {
            ids.AddRange(SceneQuery.FindAll<PersistentId>(scene));
        }

        return ids;
    }

    /// <summary>
    /// Gives every mob spawn point of <paramref name="world"/>'s chunks its id and makes every id unique within the
    /// world (<see cref="MakeUnique"/>), opening the chunk scenes that are not open and saving the scenes it changed,
    /// together (<see cref="WorldAuthoring.SaveScenes"/>): a chunk open with changes of its own that it does not change
    /// stays unsaved. Returns a line per object changed.
    /// </summary>
    public static List<string> Repair(WorldDefinition world)
    {
        List<string> report = new();
        List<Scene> opened = new();
        try
        {
            Dictionary<Vector2Int, Scene> scenes = WorldAuthoring.OpenChunkScenes(world, opened);
            List<GameObject> roots = new();
            foreach (Scene scene in scenes.Values)
            {
                roots.AddRange(scene.GetRootGameObjects());
            }

            HashSet<Scene> changedScenes = new();
            foreach (PersistentId added in AddToSpawnPoints(roots))
            {
                report.Add($"{PathOf(added)}: added a {nameof(PersistentId)}.");
                changedScenes.Add(added.gameObject.scene);
            }

            List<PersistentId> ids = new();
            foreach (Scene scene in scenes.Values)
            {
                ids.AddRange(SceneQuery.FindAll<PersistentId>(scene));
            }

            foreach (PersistentId changed in MakeUnique(ids))
            {
                report.Add($"{PathOf(changed)}: new id {changed.Id}.");
                changedScenes.Add(changed.gameObject.scene);
            }

            WorldAuthoring.SaveScenes(changedScenes.ToList());
        }
        finally
        {
            WorldAuthoring.CloseScenes(opened);
        }

        return report;
    }

    /// <summary>
    /// The scene and hierarchy path of <paramref name="component"/>'s object, to name it in messages.
    /// </summary>
    public static string PathOf(Component component)
    {
        if (component == null)
        {
            return "(destroyed)";
        }

        string path = component.name;
        for (Transform parent = component.transform.parent; parent != null; parent = parent.parent)
        {
            path = $"{parent.name}/{path}";
        }

        return $"{component.gameObject.scene.name}: {path}";
    }
}
