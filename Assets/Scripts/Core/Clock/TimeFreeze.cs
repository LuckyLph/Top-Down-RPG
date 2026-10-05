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

    /// <summary>
    /// Freezes game time on behalf of <paramref name="owner"/>; holding again with the same owner changes nothing.
    /// </summary>
    public void Hold(object owner)
    {
        if (holders.Hold(owner))
        {
            Apply();
        }
    }

    /// <summary>
    /// Drops <paramref name="owner"/>'s hold, letting time run again when no other owner still holds it.
    /// </summary>
    public void Release(object owner)
    {
        if (holders.Release(owner))
        {
            Apply();
        }
    }

    /// <summary>
    /// Drops every hold and restores a time scale of 1 when the owning scope is disposed.
    /// </summary>
    public void Dispose()
    {
        holders.Clear();
        Apply();
    }

    /// <summary>
    /// Writes the time scale for the current hold state: 0 while held, 1 otherwise.
    /// </summary>
    private void Apply()
    {
        Time.timeScale = holders.IsHeld ? 0f : 1f;
    }
}
