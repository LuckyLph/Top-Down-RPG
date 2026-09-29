using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Opens the usual multi-scene editing setups in one click.
public static class SceneSetMenu
{
    private const string OpenScenesMenu = ProjectScenes.MenuRoot + "Open Scenes/";

    [MenuItem(OpenScenesMenu + "Main", priority = 20)]
    private static void OpenMain()
    {
        Open(ProjectScenes.MainScenePath);
    }

    [MenuItem(OpenScenesMenu + "Main + Main Menu", priority = 21)]
    private static void OpenMainMenu()
    {
        GameScenes gameScenes = LoadGameScenesOrWarn();
        if (gameScenes != null)
        {
            Open(ProjectScenes.MainScenePath, gameScenes.MainMenu.ScenePath);
        }
    }

    [MenuItem(OpenScenesMenu + "Main + Gameplay + Starting Area", priority = 22)]
    private static void OpenGameplay()
    {
        GameScenes gameScenes = LoadGameScenesOrWarn();
        if (gameScenes != null)
        {
            Open(ProjectScenes.MainScenePath, gameScenes.Gameplay.ScenePath, gameScenes.StartingArea.ScenePath);
        }
    }

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

        // Edit the last scene of the set by default (menu or area), not Main.
        EditorSceneManager.SetActiveScene(EditorSceneManager.GetSceneByPath(scenePaths[scenePaths.Length - 1]));
    }

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
