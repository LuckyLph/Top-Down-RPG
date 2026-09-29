using UnityEditor;

public static class ProjectScenes
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";
    public const string GameScenesAssetPath = "Assets/Data/Scenes/GameScenes.asset";
    public const string MenuRoot = "Tools/TopDownRPG/";

    public static GameScenes LoadGameScenes()
    {
        return AssetDatabase.LoadAssetAtPath<GameScenes>(GameScenesAssetPath);
    }
}
