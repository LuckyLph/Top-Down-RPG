using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Fails a build whose scene list cannot support the Main-scene boot flow.
/// </summary>
public class SceneBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    /// <summary>
    /// Throws <see cref="BuildFailedException"/> listing every scene setup problem before the build starts.
    /// </summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        List<string> errors = Validate();
        if (errors.Count > 0)
        {
            throw new BuildFailedException("Scene setup is invalid:\n- " + string.Join("\n- ", errors));
        }
    }

    /// <summary>
    /// Runs the validation on demand and logs each problem as an error, or a single message when the list is valid.
    /// </summary>
    [MenuItem(ProjectScenes.MenuRoot + "Validate Build Scenes", priority = 40)]
    private static void ValidateFromMenu()
    {
        List<string> errors = Validate();
        if (errors.Count == 0)
        {
            Debug.Log("Build scene list is valid.");
            return;
        }

        foreach (string error in errors)
        {
            Debug.LogError(error);
        }
    }

    /// <summary>
    /// Checks that Main is the first enabled build scene and that every <see cref="SceneDefinition"/> asset points at
    /// a scene enabled in the build list, refreshing their paths first. Returns the problems found, empty when valid.
    /// </summary>
    public static List<string> Validate()
    {
        List<string> errors = new();
        List<string> buildScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToList();

        if (buildScenes.Count == 0 || buildScenes[0] != ProjectScenes.MainScenePath)
        {
            errors.Add($"'{ProjectScenes.MainScenePath}' must be the first enabled scene in the build list.");
        }

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(SceneDefinition)}"))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            SceneDefinition definition = AssetDatabase.LoadAssetAtPath<SceneDefinition>(assetPath);
            if (definition == null)
            {
                continue;
            }

            definition.RefreshPath();
            if (!definition.IsValid)
            {
                errors.Add($"SceneDefinition '{assetPath}' has no scene assigned.");
            }
            else if (!buildScenes.Contains(definition.ScenePath))
            {
                errors.Add($"Scene '{definition.ScenePath}' (from '{assetPath}') is not enabled in the build list.");
            }
        }

        return errors;
    }
}
