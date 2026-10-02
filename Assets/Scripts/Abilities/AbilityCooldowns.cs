using UnityEngine;

/// <summary>
/// Per-slot cooldown end times measured on the game clock.
/// </summary>
public sealed class AbilityCooldowns
{
    private readonly float[] readyTimes;

    public AbilityCooldowns(int slotCount)
    {
        readyTimes = new float[slotCount];
        Reset();
    }

    public int SlotCount => readyTimes.Length;

    public bool IsReady(int slot, float now)
    {
        return now >= readyTimes[slot];
    }

    public float Remaining(int slot, float now)
    {
        return Mathf.Max(0f, readyTimes[slot] - now);
    }

    public void Start(int slot, float now, float duration)
    {
        readyTimes[slot] = now + Mathf.Max(0f, duration);
    }

    public void Reset()
    {
        for (int i = 0; i < readyTimes.Length; i++)
        {
            readyTimes[i] = float.NegativeInfinity;
        }
    }
}
