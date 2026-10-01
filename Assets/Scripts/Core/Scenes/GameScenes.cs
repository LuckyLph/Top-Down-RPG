using UnityEngine;

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
    public SceneDefinition[] Areas => areas;

    public SceneDefinition FindArea(string scenePath)
    {
        return FindArea(scenePath, false);
    }

    public SceneDefinition FindAreaByGuid(string sceneGuid)
    {
        return FindArea(sceneGuid, true);
    }

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

    private static bool Matches(SceneDefinition definition, string key, bool byGuid)
    {
        if (definition == null)
        {
            return false;
        }

        return byGuid ? definition.SceneGuid == key : definition.Matches(key);
    }
}
