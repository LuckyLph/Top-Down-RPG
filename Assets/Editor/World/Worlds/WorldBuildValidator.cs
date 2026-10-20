using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Fails a build whose seamless worlds break the layout or their chunks' ownership (<see cref="WorldValidation"/>), as
/// <see cref="SceneBuildValidator"/> fails one whose scene list cannot boot. Also on the menu, to check by hand.
/// </summary>
public class WorldBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 1;

    /// <summary>
    /// Throws <see cref="BuildFailedException"/> listing every world problem before the build starts.
    /// </summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        List<string> problems = WorldValidation.ValidateAll();
        if (problems.Count > 0)
        {
            throw new BuildFailedException("The worlds are invalid:\n- " + string.Join("\n- ", problems));
        }
    }

    /// <summary>
    /// Validates every world and logs each problem as an error, or a single message when they are all valid.
    /// </summary>
    [MenuItem(ProjectScenes.MenuRoot + "World/Validate Worlds", priority = 41)]
    private static void ValidateFromMenu()
    {
        List<string> problems = WorldValidation.ValidateAll();
        if (problems.Count == 0)
        {
            Debug.Log("Every world is valid.");
            return;
        }

        foreach (string problem in problems)
        {
            Debug.LogError(problem);
        }
    }
}
