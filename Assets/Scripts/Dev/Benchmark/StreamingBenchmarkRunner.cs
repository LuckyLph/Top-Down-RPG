using System;
using System.Collections;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Dev-only entry that runs <see cref="StreamingBenchmark"/> in a player (Docs/WorldScale.md, Phase 0). Started with
/// <c>-streamingBenchmark &lt;file&gt;</c>, and optionally <c>-streamingBenchmarkLabel &lt;label&gt;</c>, it gives the
/// Main scope an in-memory save folder and settings store (<see cref="BenchmarkSaveFolder"/>,
/// <see cref="BenchmarkSaveStore"/>), so the run reads and writes no
/// save. It reads the memory used in the main menu, starts a new game (the starting world's entry, the Overworld's
/// Clearing), runs the benchmark, appends its line to the file and quits, with exit code 1 when anything failed. It
/// finds the scopes when it needs them, because they cannot reference this assembly.
/// </summary>
[DisallowMultipleComponent]
public class StreamingBenchmarkRunner : MonoBehaviour
{
    private const string FileArgument = "-streamingBenchmark";
    private const string LabelArgument = "-streamingBenchmarkLabel";
    private const string DefaultLabel = "player";
    private const float WaitTimeout = 120f;
    private const int MenuFrames = 60;
    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    private string outputPath;
    private string label;
    private IDisposable installation;

    /// <summary>
    /// When the command line asks for a run, creates the runner on a persistent game object and enqueues the in-memory
    /// stores for the Main scope, before the first scene loads and builds it.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateWhenAsked()
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        string path = ArgumentAfter(commandLine, FileArgument);
        if (path == null)
        {
            return;
        }

        GameObject host = new(nameof(StreamingBenchmarkRunner));
        DontDestroyOnLoad(host);
        StreamingBenchmarkRunner runner = host.AddComponent<StreamingBenchmarkRunner>();
        runner.outputPath = path;
        runner.label = ArgumentAfter(commandLine, LabelArgument) ?? DefaultLabel;
        runner.installation = LifetimeScope.Enqueue(builder =>
        {
            builder.RegisterInstance<ISaveFolder>(new BenchmarkSaveFolder());
            builder.RegisterInstance<ISaveStore>(new BenchmarkSaveStore()).Keyed(UserSettings.FileName);
        });
    }

    /// <summary>
    /// Starts the run.
    /// </summary>
    private void Start()
    {
        StartCoroutine(Run());
    }

    /// <summary>
    /// Stops enqueueing the stores if the run ended before the Main scope was built.
    /// </summary>
    private void OnDestroy()
    {
        installation?.Dispose();
        installation = null;
    }

    /// <summary>
    /// Waits for the Main scope and the main menu, reads the memory used there, starts a new game, waits for its world
    /// and the local player, runs the benchmark and finishes with its line.
    /// </summary>
    private IEnumerator Run()
    {
        MainLifetimeScope main = null;
        float deadline = Time.realtimeSinceStartup + WaitTimeout;
        while (main == null || main.Container == null)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Finish("the Main scope was not built", false);
                yield break;
            }

            main = FindAnyObjectByType<MainLifetimeScope>();
            yield return null;
        }

        installation.Dispose();
        installation = null;
        GameFlow gameFlow = main.Container.Resolve<GameFlow>();
        MainMenuController menu = null;
        while (menu == null)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Finish("the main menu did not open", false);
                yield break;
            }

            menu = FindAnyObjectByType<MainMenuController>();
            yield return null;
        }

        using ProfilerRecorder systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
        using ProfilerRecorder graphicsMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory", 1);
        for (int i = 0; i < MenuFrames || gameFlow.IsTransitioning; i++)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Finish("the main menu's transition did not finish", false);
                yield break;
            }

            yield return null;
        }

        string menuMemory = $"main menu memory: system {systemMemory.LastValue / BytesPerMegabyte:F0} MB, graphics {graphicsMemory.LastValue / BytesPerMegabyte:F0} MB";
        menu.StartNewGame();
        if (!gameFlow.IsTransitioning)
        {
            Finish("New Game started no transition", false);
            yield break;
        }

        deadline = Time.realtimeSinceStartup + WaitTimeout;
        LocalPlayer player = null;
        GameplayLifetimeScope gameplay = null;
        while (gameFlow.IsTransitioning || gameFlow.CurrentWorld == null || player == null)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Finish($"the new game did not start (transitioning {gameFlow.IsTransitioning}, world {(gameFlow.CurrentWorld != null ? gameFlow.CurrentWorld.name : "none")}, Gameplay scope {gameplay != null}, local player {player != null})", false);
                yield break;
            }

            yield return null;
            if (gameplay == null)
            {
                gameplay = FindAnyObjectByType<GameplayLifetimeScope>();
            }

            player = gameplay != null && gameplay.Container != null ? gameplay.Container.Resolve<LocalPlayerTracker>().Current : null;
        }

        WorldDefinition world = gameFlow.CurrentWorld;
        if (!gameplay.Container.Resolve<Worlds>().TryGet(world, out LoadedWorld loaded))
        {
            Finish($"{world.name} is not loaded", false);
            yield break;
        }

        StreamingBenchmark benchmark = new(world, loaded, gameplay.Container.Resolve<WorldStreaming>(), player.Controller);
        yield return benchmark.Run();
        if (benchmark.Failure != null)
        {
            Finish(benchmark.Failure, false);
            yield break;
        }

        Finish($"{benchmark.Line}; {menuMemory}\n{label}, loads: {benchmark.LoadDetails}", true);
    }

    /// <summary>
    /// Appends <paramref name="result"/> to the output file under the run's label (as a failure unless
    /// <paramref name="succeeded"/>) and quits with exit code 0 or 1.
    /// </summary>
    private void Finish(string result, bool succeeded)
    {
        string line = succeeded ? $"{label}: {result}" : $"{label}: failed: {result}";
        try
        {
            File.AppendAllText(outputPath, line + "\n");
        }
        catch (IOException exception)
        {
            Debug.LogError($"Could not write the benchmark's line to {outputPath}: {exception.Message}", this);
        }

        Debug.Log(line, this);
        Application.Quit(succeeded ? 0 : 1);
    }

    /// <summary>
    /// The argument after <paramref name="name"/> in <paramref name="commandLine"/>, or null when it is missing.
    /// </summary>
    private static string ArgumentAfter(string[] commandLine, string name)
    {
        for (int i = 0; i < commandLine.Length - 1; i++)
        {
            if (commandLine[i] == name)
            {
                return commandLine[i + 1];
            }
        }

        return null;
    }
}
