using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using VContainer.Unity;

// VContainer honours EnableDiagnostics in players too, so release builds would pay for collecting it.
// Turns it off for non-development builds and restores the editor setting once the build is over.
public class VContainerDiagnosticsBuildGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    private static VContainerSettings disabledSettings;

    public int callbackOrder => 0;

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

    public void OnPostprocessBuild(BuildReport report)
    {
        Restore();
    }

    private static void RestoreWhenBuildEnds()
    {
        if (BuildPipeline.isBuildingPlayer)
        {
            EditorApplication.delayCall += RestoreWhenBuildEnds;
            return;
        }

        Restore();
    }

    private static void Restore()
    {
        if (disabledSettings == null)
        {
            return;
        }

        SetDiagnostics(disabledSettings, true);
        disabledSettings = null;
    }

    private static void SetDiagnostics(VContainerSettings settings, bool enabled)
    {
        settings.EnableDiagnostics = enabled;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
    }
}
