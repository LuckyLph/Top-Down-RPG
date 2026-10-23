using System;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

/// <summary>
/// Counts the managed bytes the game's own update allocates each frame, from the start of the frame to the end of the
/// late updates: physics, scripts' updates, coroutines and late updates, but not rendering, IMGUI or the editor's own
/// loop, which allocate in the editor whatever the game does. Inserts two systems into the player loop while it lives
/// and puts the loop back when disposed.
/// </summary>
public sealed class FrameAllocationProbe : IDisposable
{
    private readonly PlayerLoopSystem original;
    private long frameStart;
    private bool started;

    /// <summary>
    /// Starts counting from the next frame.
    /// </summary>
    public FrameAllocationProbe()
    {
        original = PlayerLoop.GetCurrentPlayerLoop();
        PlayerLoopSystem root = original;
        PlayerLoopSystem[] phases = (PlayerLoopSystem[])root.subSystemList.Clone();
        for (int i = 0; i < phases.Length; i++)
        {
            if (phases[i].type == typeof(Initialization))
            {
                phases[i] = With(phases[i], Begin, true);
            }
            else if (phases[i].type == typeof(PreLateUpdate))
            {
                phases[i] = With(phases[i], End, false);
            }
        }

        root.subSystemList = phases;
        PlayerLoop.SetPlayerLoop(root);
    }

    public long Bytes { get; private set; }
    public int Frames { get; private set; }
    public long WorstFrame { get; private set; }

    /// <summary>
    /// Forgets what was counted so far.
    /// </summary>
    public void Reset()
    {
        Bytes = 0;
        Frames = 0;
        WorstFrame = 0;
    }

    /// <summary>
    /// Puts the player loop back as it was.
    /// </summary>
    public void Dispose()
    {
        PlayerLoop.SetPlayerLoop(original);
    }

    /// <summary>
    /// <paramref name="phase"/> with a system running <paramref name="update"/> first or last among its own.
    /// </summary>
    private static PlayerLoopSystem With(PlayerLoopSystem phase, PlayerLoopSystem.UpdateFunction update, bool first)
    {
        PlayerLoopSystem[] systems = phase.subSystemList ?? Array.Empty<PlayerLoopSystem>();
        PlayerLoopSystem[] grown = new PlayerLoopSystem[systems.Length + 1];
        Array.Copy(systems, 0, grown, first ? 1 : 0, systems.Length);
        grown[first ? 0 : systems.Length] = new PlayerLoopSystem { type = typeof(FrameAllocationProbe), updateDelegate = update };
        phase.subSystemList = grown;
        return phase;
    }

    /// <summary>
    /// Notes the thread's allocated bytes as the frame starts.
    /// </summary>
    private void Begin()
    {
        frameStart = GC.GetAllocatedBytesForCurrentThread();
        started = true;
    }

    /// <summary>
    /// Adds what the frame's update allocated since it started.
    /// </summary>
    private void End()
    {
        if (!started)
        {
            return;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - frameStart;
        Bytes += allocated;
        Frames++;
        WorstFrame = Math.Max(WorstFrame, allocated);
        started = false;
    }
}
