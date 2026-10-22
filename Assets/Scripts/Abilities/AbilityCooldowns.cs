using UnityEngine;

/// <summary>
/// Per-slot cooldown end times measured on the game clock.
/// </summary>
public sealed class AbilityCooldowns
{
    private readonly float[] readyTimes;

    /// <summary>
    /// Creates a cooldown table with <paramref name="slotCount"/> slots, all ready.
    /// </summary>
    public AbilityCooldowns(int slotCount)
    {
        readyTimes = new float[slotCount];
        Reset();
    }

    public int SlotCount => readyTimes.Length;

    /// <summary>
    /// Whether the cooldown of <paramref name="slot"/> has ended by clock time <paramref name="now"/>.
    /// </summary>
    public bool IsReady(int slot, float now)
    {
        return now >= readyTimes[slot];
    }

    /// <summary>
    /// Seconds left on the cooldown of <paramref name="slot"/> at clock time <paramref name="now"/>; 0 when ready.
    /// </summary>
    public float Remaining(int slot, float now)
    {
        return Mathf.Max(0f, readyTimes[slot] - now);
    }

    /// <summary>
    /// Puts <paramref name="slot"/> on cooldown until <paramref name="duration"/> seconds after
    /// <paramref name="now"/>, replacing any running cooldown; a negative duration counts as 0.
    /// </summary>
    public void Start(int slot, float now, float duration)
    {
        readyTimes[slot] = now + Mathf.Max(0f, duration);
    }

    /// <summary>
    /// The clock time the cooldown of <paramref name="slot"/> ends at; negative infinity when it never ran.
    /// </summary>
    public float ReadyTime(int slot)
    {
        return readyTimes[slot];
    }

    /// <summary>
    /// Makes the cooldown of <paramref name="slot"/> end at clock time <paramref name="readyTime"/>, for a slot that
    /// takes another ability with a cooldown of its own.
    /// </summary>
    public void SetReadyTime(int slot, float readyTime)
    {
        readyTimes[slot] = readyTime;
    }

    /// <summary>
    /// Makes every slot ready immediately.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < readyTimes.Length; i++)
        {
            readyTimes[i] = float.NegativeInfinity;
        }
    }
}
