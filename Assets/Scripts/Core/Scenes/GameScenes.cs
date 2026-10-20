using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's scene catalogue: the main menu, the gameplay scene, the starting area and spawn id, the other areas, the
/// seamless worlds, and the starting world and entry where a new game starts and where a player the session does not
/// know arrives. Only listed worlds are travelled to and restored from a save.
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
    /// The starting area or listed area whose scene is at <paramref name="scenePath"/> (the starting area first), or
    /// null for an empty path or when none is.
    /// </summary>
    public SceneDefinition FindArea(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            return null;
        }

        if (startingArea != null && startingArea.Matches(scenePath))
        {
            return startingArea;
        }

        for (int i = 0; i < areas.Length; i++)
        {
            if (areas[i] != null && areas[i].Matches(scenePath))
            {
                return areas[i];
            }
        }

        return null;
    }
}
