using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Opens the usual multi-scene editing setups in one click.
/// </summary>
public static class SceneSetMenu
{
    private const string OpenScenesMenu = ProjectScenes.MenuRoot + "Open Scenes/";

    /// <summary>
    /// Opens the Main boot scene on its own.
    /// </summary>
    [MenuItem(OpenScenesMenu + "Main", priority = 20)]
    private static void OpenMain()
    {
        Open(ProjectScenes.MainScenePath);
    }

    /// <summary>
    /// Opens Main with the main menu scene additively, making the menu active; warns and does nothing when the
    /// <see cref="GameScenes"/> asset is missing.
    /// </summary>
    [MenuItem(OpenScenesMenu + "Main + Main Menu", priority = 21)]
    private static void OpenMainMenu()
    {
        GameScenes gameScenes = LoadGameScenesOrWarn();
        if (gameScenes != null)
        {
            Open(ProjectScenes.MainScenePath, gameScenes.MainMenu.ScenePath);
        }
    }

    /// <summary>
    /// Opens Main with the Gameplay scene and the starting world's root scene additively, making the world's root
    /// active; warns and does nothing when the <see cref="GameScenes"/> asset is missing or lists no starting world.
    /// </summary>
    [MenuItem(OpenScenesMenu + "Main + Gameplay + Starting World", priority = 22)]
    private static void OpenGameplay()
    {
        GameScenes gameScenes = LoadGameScenesOrWarn();
        if (gameScenes == null)
        {
            return;
        }

        if (gameScenes.StartingWorld == null || gameScenes.StartingWorld.RootScene == null)
        {
            Debug.LogWarning($"{gameScenes.name} has no starting world with a root scene.", gameScenes);
            return;
        }

        Open(ProjectScenes.MainScenePath, gameScenes.Gameplay.ScenePath, gameScenes.StartingWorld.RootScene.ScenePath);
    }

    /// <summary>
    /// After offering to save modified scenes (aborting if declined), opens the first path alone, adds the rest and
    /// makes the last one active.
    /// </summary>
    private static void Open(params string[] scenePaths)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EditorSceneManager.OpenScene(scenePaths[0], OpenSceneMode.Single);
        for (int i = 1; i < scenePaths.Length; i++)
        {
            EditorSceneManager.OpenScene(scenePaths[i], OpenSceneMode.Additive);
        }

        EditorSceneManager.SetActiveScene(EditorSceneManager.GetSceneByPath(scenePaths[scenePaths.Length - 1]));
    }

    /// <summary>
    /// Loads the <see cref="GameScenes"/> catalog, logging a warning and returning null when the asset is missing.
    /// </summary>
    private static GameScenes LoadGameScenesOrWarn()
    {
        GameScenes gameScenes = ProjectScenes.LoadGameScenes();
        if (gameScenes == null)
        {
            Debug.LogWarning($"No GameScenes asset at '{ProjectScenes.GameScenesAssetPath}'.");
        }

        return gameScenes;
    }
}
