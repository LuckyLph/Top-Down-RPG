using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's scene catalogue: the main menu, the gameplay scene, the seamless worlds, and the starting world and entry
/// where a new game starts and where a player the session does not know arrives. Only listed worlds are travelled to and
/// restored from a save.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Scenes/Game Scenes", fileName = "GameScenes")]
public class GameScenes : ScriptableObject
{
    [SerializeField] private SceneDefinition mainMenu;
    [SerializeField] private SceneDefinition gameplay;
    [SerializeField] private WorldDefinition[] worlds = new WorldDefinition[0];
    [Tooltip("The world new and hosted games start in, and where a joining player the host does not know arrives.")]
    [SerializeField] private WorldDefinition startingWorld;
    [Tooltip("The starting world's entry; empty for its default entry.")]
    [SerializeField] private string startingWorldEntry = "start";

    public SceneDefinition MainMenu => mainMenu;
    public SceneDefinition Gameplay => gameplay;
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
}
