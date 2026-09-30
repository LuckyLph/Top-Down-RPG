using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

// On-screen stats and spawn controls for the stress scene. Pathfinding numbers come from the
// ProfilerMarkers in GridAStarPathfinder2D and MobPathAgent2D, averaged per frame over each refresh
// window. The text is rebuilt a few times per second so the overlay itself barely registers in the GC
// figure; use the Profiler for exact attribution.
public class StressTestOverlay : MonoBehaviour
{
    // Recorders only store frames in which their marker fired, summed per frame; this must hold every
    // such frame in one refresh window.
    private const int SampleCapacity = 512;
    private const float RefreshInterval = 0.25f;
    private static readonly string[] StateNames = { "Idle", "Patrol", "Chase", "Attack", "Return" };

    [SerializeField] private StressTestSpawner spawner;
    [SerializeField] private bool visible = true;
    [SerializeField, Min(0.5f)] private float scale = 1f;

    private readonly StringBuilder text = new(512);
    private readonly int[] stateCounts = new int[StateNames.Length];
    private ProfilerRecorder findPathRecorder;
    private ProfilerRecorder buildPathRecorder;
    private ProfilerRecorder gcAllocRecorder;
    private string cachedText = string.Empty;
    private float smoothedFrameSeconds;
    private float nextRefreshTime;
    private int framesSinceRefresh;

    private void OnEnable()
    {
        findPathRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "GridAStarPathfinder2D.FindPath", SampleCapacity);
        buildPathRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "MobPathAgent2D.BuildPathToWorld", SampleCapacity);
        gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", SampleCapacity);
    }

    private void OnDisable()
    {
        findPathRecorder.Dispose();
        buildPathRecorder.Dispose();
        gcAllocRecorder.Dispose();
    }

    private void Update()
    {
        float frameSeconds = Time.unscaledDeltaTime;
        smoothedFrameSeconds = smoothedFrameSeconds <= 0f ? frameSeconds : Mathf.Lerp(smoothedFrameSeconds, frameSeconds, 0.1f);
        framesSinceRefresh++;

        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            RebuildText();
        }
    }

    private void RebuildText()
    {
        System.Array.Clear(stateCounts, 0, stateCounts.Length);
        int mobCount = 0;
        if (spawner != null)
        {
            IReadOnlyList<MobController> mobs = spawner.Mobs;
            for (int i = 0; i < mobs.Count; i++)
            {
                int state = (int)mobs[i].CurrentStateId;
                if (state >= 0 && state < stateCounts.Length)
                {
                    stateCounts[state]++;
                }

                mobCount++;
            }
        }

        int frames = Mathf.Max(1, framesSinceRefresh);
        framesSinceRefresh = 0;
        ConsumePerFrame(ref findPathRecorder, frames, out double findPathMs, out double findPathCalls);
        ConsumePerFrame(ref buildPathRecorder, frames, out double buildPathMs, out double buildPathCalls);
        ConsumePerFrame(ref gcAllocRecorder, frames, out double gcBytes, out _, valueIsTime: false);

        text.Clear();
        text.Append("STRESS TEST\n");
        text.Append($"{1f / Mathf.Max(smoothedFrameSeconds, 0.0001f):F0} FPS  ({smoothedFrameSeconds * 1000f:F1} ms/frame)\n");
        text.Append($"Mobs: {mobCount}\n");
        for (int i = 0; i < StateNames.Length; i++)
        {
            text.Append($"  {StateNames[i]}: {stateCounts[i]}\n");
        }

        text.Append($"FindPath:  {findPathCalls:F1} calls, {findPathMs:F2} ms /frame\n");
        text.Append($"BuildPath: {buildPathCalls:F1} calls, {buildPathMs:F2} ms /frame\n");
        text.Append($"GC alloc:  {gcBytes / 1024d:F1} KB /frame");
        cachedText = text.ToString();
    }

    // Per-frame mean of everything recorded since the last call, then clears the recorder. Frames in
    // which the marker never fired have no sample, so the total is divided by elapsed frames rather
    // than by the sample count. Time recorders report nanoseconds.
    private static void ConsumePerFrame(ref ProfilerRecorder recorder, int frames, out double value, out double calls, bool valueIsTime = true)
    {
        value = 0d;
        calls = 0d;
        if (!recorder.Valid)
        {
            return;
        }

        for (int i = 0; i < recorder.Count; i++)
        {
            ProfilerRecorderSample sample = recorder.GetSample(i);
            value += sample.Value;
            calls += sample.Count;
        }

        // Reset also stops collection, so restart it for the next window.
        recorder.Reset();
        recorder.Start();
        value /= frames;
        calls /= frames;
        if (valueIsTime)
        {
            value *= 1e-6d;
        }
    }

    private void OnGUI()
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        // Bottom-left, clear of the HUD's health bar.
        const float width = 260f;
        const float height = 300f;
        float top = Screen.height / scale - height - 10f;
        if (!visible)
        {
            if (GUI.Button(new Rect(10f, top + height - 24f, 110f, 24f), "Show stats"))
            {
                visible = true;
            }

            GUI.matrix = previousMatrix;
            return;
        }

        GUI.Box(new Rect(10f, top, width, height), GUIContent.none);
        GUI.Label(new Rect(18f, top + 4f, width - 16f, 190f), cachedText);

        float y = top + 196f;
        if (spawner != null)
        {
            if (GUI.Button(new Rect(18f, y, 118f, 24f), "+10 near player"))
            {
                spawner.SpawnNearPlayer(10);
            }

            if (GUI.Button(new Rect(144f, y, 118f, 24f), "+50 near player"))
            {
                spawner.SpawnNearPlayer(50);
            }

            y += 30f;
            if (GUI.Button(new Rect(18f, y, 118f, 24f), "+50 across map"))
            {
                spawner.SpawnAcrossMap(50);
            }

            if (GUI.Button(new Rect(144f, y, 118f, 24f), "+200 across map"))
            {
                spawner.SpawnAcrossMap(200);
            }

            y += 30f;
            if (GUI.Button(new Rect(18f, y, 118f, 24f), "Clear mobs"))
            {
                spawner.ClearMobs();
            }
        }

        if (GUI.Button(new Rect(144f, y, 118f, 24f), "Hide"))
        {
            visible = false;
        }

        GUI.matrix = previousMatrix;
    }
}
