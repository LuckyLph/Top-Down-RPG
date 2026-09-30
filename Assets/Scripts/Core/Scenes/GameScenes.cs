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
