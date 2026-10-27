using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dev-only benchmark of streaming a world (Docs/WorldScale.md, Phase 0), shared by the Overworld's PlayMode benchmark
/// and a player started with <c>-streamingBenchmark</c> (<see cref="StreamingBenchmarkRunner"/>). Once the chunks
/// around the first entry of <see cref="Route"/> are up and warm, the player is carried a fixed step a frame from
/// entry to entry along it, its mobs deactivated as their chunks load. <see cref="Line"/> then reports the frames,
/// their times and main thread times, the chunks loaded with the worst frame of each load's first few and the scene
/// activation and ground build in them, with the mean of each of the build's parts and of the neighbours' skirt
/// re-reads and teardowns (Docs/WorldScale.md, Phase 2), the memory allocated a frame, the draw calls, the most chunks
/// and props loaded at once, and the memory used.
/// </summary>
public sealed class StreamingBenchmark
{
    public const int WarmFrames = 120;
    public const float Step = 0.25f;
    public const int LoadFrames = 3;
    private const float SettleTimeout = 60f;
    private const double NanosecondsPerMillisecond = 1e6;
    private const double BytesPerMegabyte = 1024.0 * 1024.0;
    private const string ActivationMarker = "Application.LoadLevelAsync Integrate";
    private const string GroundMarker = "TerrainBlendRenderer.Build";
    private const string GroundPartPrefix = "TerrainBlendRenderer.";

    private static readonly string[] route = { "clearing", "villageForest", "coast", "snowfield", "clearing", "lakeside", "lavaFields" };
    private static readonly string[] groundParts = { "ControlMap", "CoastMap", "Arrays", "ElevationMap", "Lut", "Material", "Chunks", "Skirt", "Teardown" };

    private readonly WorldDefinition world;
    private readonly LoadedWorld loaded;
    private readonly WorldStreaming streaming;
    private readonly PlayerController player;

    /// <summary>
    /// A benchmark of streaming <paramref name="world"/>, loaded as <paramref name="loaded"/> by
    /// <paramref name="streaming"/>, carrying <paramref name="player"/>.
    /// </summary>
    public StreamingBenchmark(WorldDefinition world, LoadedWorld loaded, WorldStreaming streaming, PlayerController player)
    {
        this.world = world;
        this.loaded = loaded;
        this.streaming = streaming;
        this.player = player;
    }

    /// <summary>
    /// The entries the player is carried between, in order: from the Clearing through every region of the Overworld.
    /// </summary>
    public static IReadOnlyList<string> Route => route;

    /// <summary>
    /// What the run measured, once <see cref="Run"/> has finished without a <see cref="Failure"/>.
    /// </summary>
    public string Line { get; private set; }

    /// <summary>
    /// Why the run stopped early, or null.
    /// </summary>
    public string Failure { get; private set; }

    /// <summary>
    /// Each chunk load the run measured, in order, once <see cref="Run"/> has finished: its scene, the props it holds,
    /// the worst frame of the load and the frames after it, and the scene activation and ground build in them, with the
    /// build's parts.
    /// </summary>
    public string LoadDetails { get; private set; }

    /// <summary>
    /// Places the player on the route's first entry, waits for the chunks around it and warms up, then carries the
    /// player along the route a step a frame and sets <see cref="Line"/>, or <see cref="Failure"/> when the chunks
    /// around the start never load.
    /// </summary>
    public IEnumerator Run()
    {
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        player.GetComponent<Collider2D>().enabled = false;
        Vector2 start = world.EntryPosition(route[0]);
        player.Teleport(start);
        float deadline = Time.realtimeSinceStartup + SettleTimeout;
        while (!streaming.IsSettled(world, start))
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Failure = $"the chunks around {route[0]} did not load within {SettleTimeout} s";
                yield break;
            }

            yield return null;
        }

        for (int i = 0; i < WarmFrames; i++)
        {
            DeactivateMobs();
            yield return null;
        }

        int chunkLoads = 0;
        int loadedThisFrame = 0;
        int framesSinceLoad = LoadFrames + 1;
        List<Scene> scenesThisFrame = new();
        void CountLoad(Scene scene, LoadSceneMode mode)
        {
            loadedThisFrame++;
            scenesThisFrame.Add(scene);
        }

        using ProfilerRecorder mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        using ProfilerRecorder allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        using ProfilerRecorder activation = ProfilerRecorder.StartNew(ProfilerCategory.Loading, ActivationMarker, 1);
        using ProfilerRecorder ground = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, GroundMarker, 1);
        using ProfilerRecorder systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
        using ProfilerRecorder graphicsMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory", 1);
#if !UNITY_EDITOR
        using ProfilerRecorder drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
#endif
        yield return null;
        int warmChunks = loaded.Chunks.Count;
        long warmSystem = systemMemory.LastValue;
        long warmGraphics = graphicsMemory.LastValue;
        long mostSystem = warmSystem;
        long mostGraphics = warmGraphics;
        List<double> frameTimes = new();
        List<double> mainTimes = new();
        List<double> loadFrameTimes = new();
        List<double> loadActivations = new();
        List<double> loadGrounds = new();
        List<double[]> loadGroundParts = new();
        List<Scene> loadScenes = new();
        List<string> details = new();
        double allocatedSum = 0;
        double drawSum = 0;
        int mostChunks = warmChunks;
        int mostProps = CountProps();
        float startTime = Time.realtimeSinceStartup;
        ProfilerRecorder[] groundPartRecorders = new ProfilerRecorder[groundParts.Length];
        SceneManager.sceneLoaded += CountLoad;
        try
        {
            for (int i = 0; i < groundParts.Length; i++)
            {
                groundPartRecorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, GroundPartPrefix + groundParts[i], 1);
            }

            for (int leg = 1; leg < route.Length; leg++)
            {
                Vector2 from = world.EntryPosition(route[leg - 1]);
                Vector2 to = world.EntryPosition(route[leg]);
                int steps = Mathf.CeilToInt(Vector2.Distance(from, to) / Step);
                for (int step = 1; step <= steps; step++)
                {
                    Vector2 at = Vector2.Lerp(from, to, step / (float)steps);
                    body.position = at;
                    player.transform.position = at;
                    DeactivateMobs();
                    loadedThisFrame = 0;
                    scenesThisFrame.Clear();
                    yield return null;
                    double frame = Time.unscaledDeltaTime * 1000.0;
                    frameTimes.Add(frame);
                    mainTimes.Add(mainThread.LastValue / NanosecondsPerMillisecond);
                    allocatedSum += allocated.LastValue;
#if UNITY_EDITOR
                    drawSum += UnityEditor.UnityStats.drawCalls;
#else
                    drawSum += drawCalls.LastValue;
#endif
                    mostSystem = Math.Max(mostSystem, systemMemory.LastValue);
                    mostGraphics = Math.Max(mostGraphics, graphicsMemory.LastValue);
                    if (loadedThisFrame > 0)
                    {
                        chunkLoads += loadedThisFrame;
                        loadFrameTimes.Add(0);
                        loadActivations.Add(0);
                        loadGrounds.Add(0);
                        loadGroundParts.Add(new double[groundParts.Length]);
                        loadScenes.Add(scenesThisFrame[0]);
                        framesSinceLoad = 0;
                        mostChunks = Mathf.Max(mostChunks, loaded.Chunks.Count);
                    }

                    if (framesSinceLoad <= LoadFrames)
                    {
                        int last = loadFrameTimes.Count - 1;
                        loadFrameTimes[last] = Math.Max(loadFrameTimes[last], frame);
                        loadActivations[last] += activation.LastValue / NanosecondsPerMillisecond;
                        loadGrounds[last] += ground.LastValue / NanosecondsPerMillisecond;
                        for (int i = 0; i < groundParts.Length; i++)
                        {
                            loadGroundParts[last][i] += groundPartRecorders[i].LastValue / NanosecondsPerMillisecond;
                        }

                        framesSinceLoad++;
                        if (framesSinceLoad > LoadFrames)
                        {
                            mostProps = Mathf.Max(mostProps, CountProps());
                            details.Add($"{loadScenes[last].name} with {CountProps(loadScenes[last])} props: worst frame {loadFrameTimes[last]:F1} ms, activation {loadActivations[last]:F1} ms, ground {loadGrounds[last]:F1} ms ({GroundPartsText(loadGroundParts[last])})");
                        }
                    }
                }
            }
        }
        finally
        {
            SceneManager.sceneLoaded -= CountLoad;
            foreach (ProfilerRecorder recorder in groundPartRecorders)
            {
                recorder.Dispose();
            }
        }

        double[] meanGroundParts = new double[groundParts.Length];
        foreach (double[] parts in loadGroundParts)
        {
            for (int i = 0; i < groundParts.Length; i++)
            {
                meanGroundParts[i] += parts[i] / loadGroundParts.Count;
            }
        }

        float seconds = Time.realtimeSinceStartup - startTime;
        int frames = frameTimes.Count;
        Line = $"{frames} frames over {seconds:F1} s; frame {Mean(frameTimes):F2} ms mean, {Percentile(frameTimes, 0.99f):F1} ms p99, {Max(frameTimes):F1} ms worst; "
            + $"main thread {Mean(mainTimes):F2} ms mean, {Percentile(mainTimes, 0.99f):F1} ms p99, {Max(mainTimes):F1} ms worst; "
            + $"{chunkLoads} chunk loads, the worst frame of each load and the {LoadFrames} after it {Mean(loadFrameTimes):F1} ms mean, {Max(loadFrameTimes):F1} ms worst, "
            + $"scene activation in them {Mean(loadActivations):F1} ms mean, {Max(loadActivations):F1} ms worst, ground build {Mean(loadGrounds):F1} ms mean, {Max(loadGrounds):F1} ms worst, its parts' means {GroundPartsText(meanGroundParts)}; "
            + $"{allocatedSum / Math.Max(frames, 1) / 1024:F1} KB allocated a frame; {drawSum / Math.Max(frames, 1):F0} draw calls; at most {mostChunks} chunks and {mostProps} props loaded; "
            + $"memory with {warmChunks} chunks warm: system {warmSystem / BytesPerMegabyte:F0} MB, graphics {warmGraphics / BytesPerMegabyte:F0} MB, at most {mostSystem / BytesPerMegabyte:F0} and {mostGraphics / BytesPerMegabyte:F0} MB; "
            + $"focused {Application.isFocused}";
        LoadDetails = string.Join("; ", details);
    }

    /// <summary>
    /// The mean of <paramref name="values"/>, 0 for none.
    /// </summary>
    public static double Mean(IReadOnlyList<double> values)
    {
        double sum = 0;
        foreach (double value in values)
        {
            sum += value;
        }

        return values.Count > 0 ? sum / values.Count : 0;
    }

    /// <summary>
    /// The largest of <paramref name="values"/>, 0 for none.
    /// </summary>
    public static double Max(IReadOnlyList<double> values)
    {
        double max = 0;
        foreach (double value in values)
        {
            max = Math.Max(max, value);
        }

        return max;
    }

    /// <summary>
    /// The value of <paramref name="values"/> at <paramref name="share"/> of the way up their sorted order (the
    /// smallest value at least that share of them reach), 0 for none.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> values, float share)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = new(values);
        sorted.Sort();
        return sorted[Mathf.Clamp(Mathf.CeilToInt(share * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    /// <summary>
    /// The ground build's parts with their times in milliseconds, named after their markers.
    /// </summary>
    private static string GroundPartsText(double[] times)
    {
        string[] parts = new string[groundParts.Length];
        for (int i = 0; i < groundParts.Length; i++)
        {
            parts[i] = $"{groundParts[i]} {times[i]:F2}";
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Deactivates every loaded mob, so none attacks or blocks the player during the run.
    /// </summary>
    private static void DeactivateMobs()
    {
        foreach (MobController mob in UnityEngine.Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// How many props are loaded, counted by their fade.
    /// </summary>
    private static int CountProps()
    {
        return UnityEngine.Object.FindObjectsByType<PropFade>().Length;
    }

    /// <summary>
    /// How many props <paramref name="scene"/> holds, counted by their fade, or 0 once it is unloaded.
    /// </summary>
    private static int CountProps(Scene scene)
    {
        if (!scene.isLoaded)
        {
            return 0;
        }

        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            count += root.GetComponentsInChildren<PropFade>(true).Length;
        }

        return count;
    }
}
