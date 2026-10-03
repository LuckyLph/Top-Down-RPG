using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns <see cref="Time.timeScale"/>: game time stands still (scale 0) while any owner holds the freeze and runs
/// (scale 1) once the last one releases it, and again when disposed. Releasing an owner that holds nothing does nothing.
/// </summary>
public sealed class TimeFreeze : IDisposable
{
    private readonly HashSet<object> holders = new();

    public bool IsFrozen => holders.Count > 0;

    public void Hold(object owner)
    {
        if (owner != null && holders.Add(owner))
        {
            Apply();
        }
    }

    public void Release(object owner)
    {
        if (owner != null && holders.Remove(owner))
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
        Time.timeScale = holders.Count > 0 ? 0f : 1f;
    }
}
