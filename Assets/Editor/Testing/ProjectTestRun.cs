using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the project's tests for tools that drive the editor (UnityMCP's execute_code) and writes a short summary to
/// <see cref="SummaryPath"/> when the run ends, so one call starts a run and one wait on that file reads its outcome:
/// the counts, the slowest test classes with their times, and each failure's first message line. A run refuses to
/// start while the editor compiles, plays or runs another, or while an open scene has unsaved changes, since it makes
/// an empty scene active first (EditMode tests touch the active scene). Its callbacks are registered on every domain
/// load, so a PlayMode run still writes its summary after the reloads around play mode. Docs/Architecture.md
/// describes the workflow.
/// </summary>
[InitializeOnLoad]
public static class ProjectTestRun
{
    public const string SummaryPath = "Temp/TestRunSummary.txt";
    private const string RunningKey = "TopDownRPG.ProjectTestRun.Running";
    private const string FilterKey = "TopDownRPG.ProjectTestRun.Filter";
    private const string SlowestKey = "TopDownRPG.ProjectTestRun.Slowest";

    static ProjectTestRun()
    {
        ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new Callbacks());
    }

    /// <summary>
    /// Starts a run of <paramref name="mode"/> ("EditMode" or "PlayMode") tests narrowed to the test classes in
    /// <paramref name="fixtures"/>, the categories in <paramref name="categories"/> and the full test names in
    /// <paramref name="tests"/>, each a list separated by semicolons (empty for no narrowing; the full suite when all
    /// three are empty). Explicit tests run only when named or when their category is. The summary lists the
    /// <paramref name="slowest"/> slowest test classes and tests. Returns "started" or why the run did not start; the
    /// summary appears at <see cref="SummaryPath"/> once it ends.
    /// </summary>
    public static string Start(string mode, string fixtures = "", string categories = "", string tests = "", int slowest = 8)
    {
        if (!Enum.TryParse(mode, out TestMode testMode))
        {
            return $"refused: unknown mode {mode}; use EditMode or PlayMode";
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return "refused: the editor is compiling or importing";
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return "refused: the editor is in play mode";
        }

        if (SessionState.GetBool(RunningKey, false))
        {
            return "refused: a run is already going";
        }

        List<string> dirty = new();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
            {
                dirty.Add(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path);
            }
        }

        if (dirty.Count > 0)
        {
            return "refused: unsaved changes in " + string.Join(", ", dirty);
        }

        File.Delete(SummaryPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Filter filter = new()
        {
            testMode = testMode,
            groupNames = Split(fixtures).Select(name => "^" + Regex.Escape(name) + "\\.").ToArray(),
            categoryNames = Split(categories),
            testNames = Split(tests),
        };
        string description = $"{mode} fixtures [{fixtures}] categories [{categories}] tests [{tests}]";
        SessionState.SetBool(RunningKey, true);
        SessionState.SetString(FilterKey, description);
        SessionState.SetInt(SlowestKey, slowest);
        ScriptableObject.CreateInstance<TestRunnerApi>().Execute(new ExecutionSettings(filter));
        return "started " + description;
    }

    /// <summary>
    /// The non-empty entries of the semicolon-separated <paramref name="list"/>.
    /// </summary>
    private static string[] Split(string list)
    {
        return (list ?? "").Split(';').Select(entry => entry.Trim()).Where(entry => entry.Length > 0).ToArray();
    }

    /// <summary>
    /// The summary of a finished run: its outcome, counts and length, the slowest test classes and tests, and every
    /// failure.
    /// </summary>
    private static string Summarize(ITestResultAdaptor result, string description, int slowest)
    {
        List<ITestResultAdaptor> leaves = new();
        List<ITestResultAdaptor> fixtures = new();
        Collect(result, leaves, fixtures);
        int passed = leaves.Count(leaf => leaf.TestStatus == TestStatus.Passed);
        int failed = leaves.Count(leaf => leaf.TestStatus == TestStatus.Failed);
        int skipped = leaves.Count(leaf => leaf.TestStatus == TestStatus.Skipped);
        int inconclusive = leaves.Count(leaf => leaf.TestStatus == TestStatus.Inconclusive);
        StringBuilder summary = new();
        summary.AppendLine($"{(failed > 0 ? "FAILED" : "PASSED")} {description}");
        summary.AppendLine($"{leaves.Count} tests: {passed} passed, {failed} failed, {skipped} skipped, {inconclusive} inconclusive, in {result.Duration:0.0} s");
        summary.AppendLine("Slowest test classes:");
        foreach (ITestResultAdaptor fixture in fixtures.OrderByDescending(fixture => fixture.Duration).Take(slowest))
        {
            summary.AppendLine($"  {fixture.Name} {fixture.Duration:0.0} s");
        }

        summary.AppendLine("Slowest tests:");
        foreach (ITestResultAdaptor leaf in leaves.OrderByDescending(leaf => leaf.Duration).Take(slowest))
        {
            summary.AppendLine($"  {leaf.FullName} {leaf.Duration:0.00} s");
        }

        foreach (ITestResultAdaptor leaf in leaves.Where(leaf => leaf.TestStatus == TestStatus.Failed))
        {
            string message = (leaf.Message ?? "").Trim();
            int lineEnd = message.IndexOfAny(new[] { '\r', '\n' });
            summary.AppendLine($"FAIL {leaf.FullName}: {(lineEnd >= 0 ? message.Substring(0, lineEnd) : message)}");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Adds every test case under <paramref name="node"/> to <paramref name="leaves"/> and every test class to
    /// <paramref name="fixtures"/>.
    /// </summary>
    private static void Collect(ITestResultAdaptor node, List<ITestResultAdaptor> leaves, List<ITestResultAdaptor> fixtures)
    {
        if (!node.HasChildren && !node.Test.IsSuite)
        {
            leaves.Add(node);
            return;
        }

        if (node.Test.IsSuite && node.Test.TypeInfo != null && node.Test.Method == null)
        {
            fixtures.Add(node);
        }

        foreach (ITestResultAdaptor child in node.Children)
        {
            Collect(child, leaves, fixtures);
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <see cref="SummaryPath"/> whole, through a temporary file, so a reader never
    /// sees half of it, and ends the run.
    /// </summary>
    private static void Finish(string text)
    {
        string partial = SummaryPath + ".partial";
        File.WriteAllText(partial, text);
        File.Delete(SummaryPath);
        File.Move(partial, SummaryPath);
        SessionState.EraseBool(RunningKey);
        SessionState.EraseString(FilterKey);
        SessionState.EraseInt(SlowestKey);
    }

    /// <summary>
    /// Writes the summary when a run this class started ends, or the error when it cannot run.
    /// </summary>
    private sealed class Callbacks : IErrorCallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (SessionState.GetBool(RunningKey, false))
            {
                Finish(Summarize(result, SessionState.GetString(FilterKey, ""), SessionState.GetInt(SlowestKey, 8)));
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }

        public void OnError(string message)
        {
            if (SessionState.GetBool(RunningKey, false))
            {
                Finish($"ERROR {SessionState.GetString(FilterKey, "")}\n{message}\n");
            }
        }
    }
}
