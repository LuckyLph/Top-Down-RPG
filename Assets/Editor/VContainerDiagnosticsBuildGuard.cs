using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using VContainer.Unity;

/// <summary>
/// VContainer honours EnableDiagnostics in players too, so release builds would pay for collecting it.
/// Turns it off for non-development builds and restores the editor setting once the build is over.
/// </summary>
public class VContainerDiagnosticsBuildGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    private static VContainerSettings disabledSettings;

    public int callbackOrder => 0;

    /// <summary>
    /// For a non-development build, switches off diagnostics on the preloaded <see cref="VContainerSettings"/> if they
    /// are on, remembers that it did, and schedules a restore that also covers failed builds.
    /// </summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        if ((report.summary.options & BuildOptions.Development) != 0)
        {
            return;
        }

        VContainerSettings settings = PlayerSettings.GetPreloadedAssets().OfType<VContainerSettings>().FirstOrDefault();
        if (settings == null || !settings.EnableDiagnostics)
        {
            return;
        }

        disabledSettings = settings;
        SetDiagnostics(settings, false);

        // Postprocess callbacks are skipped when a build fails, so also restore once it has ended.
        EditorApplication.delayCall += RestoreWhenBuildEnds;
    }

    /// <summary>
    /// Restores diagnostics right after a successful build.
    /// </summary>
    public void OnPostprocessBuild(BuildReport report)
    {
        Restore();
    }

    /// <summary>
    /// Re-queues itself on the next editor tick until no player build is running, then restores diagnostics.
    /// </summary>
    private static void RestoreWhenBuildEnds()
    {
        if (BuildPipeline.isBuildingPlayer)
        {
            EditorApplication.delayCall += RestoreWhenBuildEnds;
            return;
        }

        Restore();
    }

    /// <summary>
    /// Turns diagnostics back on for the settings this guard disabled, once; does nothing when it disabled none.
    /// </summary>
    private static void Restore()
    {
        if (disabledSettings == null)
        {
            return;
        }

        SetDiagnostics(disabledSettings, true);
        disabledSettings = null;
    }

    /// <summary>
    /// Writes the diagnostics flag on <paramref name="settings"/> and saves the asset.
    /// </summary>
    private static void SetDiagnostics(VContainerSettings settings, bool enabled)
    {
        settings.EnableDiagnostics = enabled;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
    }
}
