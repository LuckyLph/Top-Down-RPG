using System;
using UnityEngine;

/// <summary>
/// Owns <see cref="Time.timeScale"/>: game time stands still (scale 0) while any owner holds the freeze and runs
/// (scale 1) once the last one releases it, and again when disposed. Releasing an owner that holds nothing does nothing.
/// </summary>
public sealed class TimeFreeze : IDisposable
{
    private readonly HoldSet holders = new();

    public bool IsFrozen => holders.IsHeld;

    public void Hold(object owner)
    {
        if (holders.Hold(owner))
        {
            Apply();
        }
    }

    public void Release(object owner)
    {
        if (holders.Release(owner))
        {
            Apply();
        }
    }

    public void Dispose()
    {
        holders.Clear();
        Apply();
    }

    private void Apply()
    {
        Time.timeScale = holders.IsHeld ? 0f : 1f;
    }
}
