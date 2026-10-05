using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes Play always start from the Main scene, like a build, while remembering which scenes were
/// open so BootFlow can load them (e.g. press Play with an area open to play that area).
/// Test Runner play sessions are left alone: they must start in the runner's own init scene.
/// </summary>
[InitializeOnLoad]
public static class PlayModeBootstrapper
{
    private const string EnabledPrefKey = "TopDownRPG.BootFromMain";
    private const string ToggleMenuPath = ProjectScenes.MenuRoot + "Boot From Main";
    private const string TestRunnerScenePrefix = "InitTestScene";

    /// <summary>
    /// Hooks play mode changes and applies the start scene once the editor has finished loading.
    /// </summary>
    static PlayModeBootstrapper()
    {
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.delayCall += Apply;
    }

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPrefKey, true);
        set
        {
            EditorPrefs.SetBool(EnabledPrefKey, value);
            Apply();
        }
    }

    /// <summary>
    /// Flips booting from Main on or off from the Tools menu.
    /// </summary>
    [MenuItem(ToggleMenuPath, priority = 0)]
    private static void Toggle()
    {
        Enabled = !Enabled;
    }

    /// <summary>
    /// Keeps the menu checkmark in step with the setting; the entry is always available.
    /// </summary>
    [MenuItem(ToggleMenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(ToggleMenuPath, Enabled);
        return true;
    }

    /// <summary>
    /// Sets the editor's play mode start scene to Main when enabled, or clears it so Play starts in the open scenes.
    /// </summary>
    public static void Apply()
    {
        EditorSceneManager.playModeStartScene = Enabled
            ? AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectScenes.MainScenePath)
            : null;
    }

    /// <summary>
    /// Reapplies the start scene on return to Edit Mode. When leaving Edit Mode with booting enabled, clears the start
    /// scene and boot request for a Test Runner session, or else saves modified scenes (cancelling Play if the user
    /// declines) and records the open scenes as the boot request.
    /// </summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            // Restores the start scene after a Test Runner session cleared it.
            Apply();
            return;
        }

        if (change != PlayModeStateChange.ExitingEditMode || !Enabled)
        {
            return;
        }

        // The Test Runner fires its RunStarted callback only after entering play mode, so detect
        // its bootstrap scene instead; the start scene is still read after this callback.
        if (IsTestRunnerSessionStarting())
        {
            EditorSceneManager.playModeStartScene = null;
            EditorBootRequest.Clear();
            return;
        }

        // Play loads scenes from disk, so unsaved edits would silently be missing.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorApplication.isPlaying = false;
            return;
        }

        Apply();
        EditorBootRequest.Set(GetOpenScenePaths());
    }

    /// <summary>
    /// Whether a loaded scene is the Test Runner's InitTestScene, meaning Play was started by a test run.
    /// </summary>
    private static bool IsTestRunnerSessionStarting()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).name.StartsWith(TestRunnerScenePrefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Paths of the open scenes that are saved to disk, excluding Main.
    /// </summary>
    private static string[] GetOpenScenePaths()
    {
        List<string> paths = new();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path) && scene.path != ProjectScenes.MainScenePath)
            {
                paths.Add(scene.path);
            }
        }

        return paths.ToArray();
    }
}
