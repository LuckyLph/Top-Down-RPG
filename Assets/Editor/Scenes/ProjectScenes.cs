using UnityEditor;

/// <summary>
/// Shared editor paths for the boot scene, the scene catalog asset and the project's Tools menu.
/// </summary>
public static class ProjectScenes
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";
    public const string GameScenesAssetPath = "Assets/Data/Scenes/GameScenes.asset";
    public const string MenuRoot = "Tools/TopDownRPG/";

    /// <summary>
    /// Loads the project's <see cref="GameScenes"/> catalog asset, or null when it does not exist.
    /// </summary>
    public static GameScenes LoadGameScenes()
    {
        return AssetDatabase.LoadAssetAtPath<GameScenes>(GameScenesAssetPath);
    }
}
