using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SliceVerificationCommands
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string BuildRelativePath = "Builds/Phase01/TopDownRPGPhase01.exe";
    private const string BuildResultRelativePath = "TestResults/Phase01/Smoke/BuildResult.json";

    [MenuItem("Tools/Top Down RPG/Verification/Build Phase 01 Development Player")]
    public static void BuildPhase01DevelopmentPlayer()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string buildPath = Path.Combine(projectRoot, BuildRelativePath);
        string buildResultPath = Path.Combine(projectRoot, BuildResultRelativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(buildPath));
        Directory.CreateDirectory(Path.GetDirectoryName(buildResultPath));

        BuildVerificationResult result;

        try
        {
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = buildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.StrictMode
            });

            result = new BuildVerificationResult
            {
                result = report.summary.result.ToString(),
                message = CreateSummaryMessage(report.summary),
                outputPath = report.summary.outputPath,
                totalErrors = report.summary.totalErrors,
                totalWarnings = report.summary.totalWarnings
            };
        }
        catch (Exception exception)
        {
            result = new BuildVerificationResult
            {
                result = "Exception",
                message = exception.Message,
                outputPath = buildPath,
                totalErrors = 1,
                totalWarnings = 0
            };

            Debug.LogError($"Phase 01 Development Build threw an exception: {exception}");
        }

        File.WriteAllText(buildResultPath, JsonUtility.ToJson(result, true));
        Debug.Log($"Phase 01 Development Build result: {result.result}. Evidence: {buildResultPath}");
    }

    private static string CreateSummaryMessage(BuildSummary summary)
    {
        return $"Build result: {summary.result}. Errors: {summary.totalErrors}. Warnings: {summary.totalWarnings}.";
    }

    [Serializable]
    private sealed class BuildVerificationResult
    {
        public string result;
        public string message;
        public string outputPath;
        public int totalErrors;
        public int totalWarnings;
    }
}
