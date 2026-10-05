using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's scene catalogue: the main menu, the gameplay scene, the starting area and spawn id, and the other
/// areas. Only areas listed here (the starting one included) can be saved or followed by clients.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Scenes/Game Scenes", fileName = "GameScenes")]
public class GameScenes : ScriptableObject
{
    [SerializeField] private SceneDefinition mainMenu;
    [SerializeField] private SceneDefinition gameplay;
    [SerializeField] private SceneDefinition startingArea;
    [SerializeField] private string startingSpawnId = "start";
    [SerializeField] private SceneDefinition[] areas = new SceneDefinition[0];

    public SceneDefinition MainMenu => mainMenu;
    public SceneDefinition Gameplay => gameplay;
    public SceneDefinition StartingArea => startingArea;
    public string StartingSpawnId => startingSpawnId;
    public IReadOnlyList<SceneDefinition> Areas => areas;

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
