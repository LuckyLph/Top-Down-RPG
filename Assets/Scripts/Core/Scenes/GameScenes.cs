using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's scene catalogue: the main menu, the gameplay scene, the starting area and spawn id, the other areas, the
/// seamless worlds, and the starting world and entry where hosted games start and where a player the host does not
/// know arrives. Only areas listed here (the starting one included) can be saved, and only listed worlds travelled to.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Scenes/Game Scenes", fileName = "GameScenes")]
public class GameScenes : ScriptableObject
{
    [SerializeField] private SceneDefinition mainMenu;
    [SerializeField] private SceneDefinition gameplay;
    [SerializeField] private SceneDefinition startingArea;
    [SerializeField] private string startingSpawnId = "start";
    [SerializeField] private SceneDefinition[] areas = new SceneDefinition[0];
    [SerializeField] private WorldDefinition[] worlds = new WorldDefinition[0];
    [Tooltip("The world hosted games start in, and where a joining player the host does not know arrives.")]
    [SerializeField] private WorldDefinition startingWorld;
    [Tooltip("The starting world's entry; empty for its default entry.")]
    [SerializeField] private string startingWorldEntry = "start";

    public SceneDefinition MainMenu => mainMenu;
    public SceneDefinition Gameplay => gameplay;
    public SceneDefinition StartingArea => startingArea;
    public string StartingSpawnId => startingSpawnId;
    public IReadOnlyList<SceneDefinition> Areas => areas;
    public IReadOnlyList<WorldDefinition> Worlds => worlds;
    public WorldDefinition StartingWorld => startingWorld;
    public string StartingWorldEntry => startingWorldEntry;

    /// <summary>
    /// The listed world whose root scene or one of whose chunk scenes is at <paramref name="scenePath"/>, or null.
    /// </summary>
    public WorldDefinition FindWorldOwning(string scenePath)
    {
        foreach (WorldDefinition world in worlds)
        {
            if (world != null && world.OwnsScene(scenePath))
            {
                return world;
            }
        }

        return null;
    }

    /// <summary>
    /// The listed world with the id <paramref name="worldId"/>, or null.
    /// </summary>
    public WorldDefinition FindWorldById(string worldId)
    {
        foreach (WorldDefinition world in worlds)
        {
            if (world != null && !string.IsNullOrEmpty(worldId) && world.WorldId == worldId)
            {
                return world;
            }
        }

        return null;
    }

    /// <summary>
    /// The starting area or listed area whose scene is at <paramref name="scenePath"/>, or null when none is.
    /// </summary>
    public SceneDefinition FindArea(string scenePath)
    {
        return FindArea(scenePath, false);
    }

    /// <summary>
    /// The starting area or listed area whose scene asset has <paramref name="sceneGuid"/>, or null when none has.
    /// </summary>
    public SceneDefinition FindAreaByGuid(string sceneGuid)
    {
        return FindArea(sceneGuid, true);
    }

    /// <summary>
    /// Searches the starting area, then the area list, for a definition matching <paramref name="key"/> as a GUID or
    /// a path; null for an empty key or no match.
    /// </summary>
    private SceneDefinition FindArea(string key, bool byGuid)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (Matches(startingArea, key, byGuid))
        {
            return startingArea;
        }

        for (int i = 0; i < areas.Length; i++)
        {
            if (Matches(areas[i], key, byGuid))
            {
                return areas[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a non-null <paramref name="definition"/> has <paramref name="key"/> as its scene GUID or path.
    /// </summary>
    private static bool Matches(SceneDefinition definition, string key, bool byGuid)
    {
        if (definition == null)
        {
            return false;
        }

        return byGuid ? definition.SceneGuid == key : definition.Matches(key);
    }
}
