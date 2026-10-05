using System.Collections.Generic;

/// <summary>
/// The owners currently holding something on (a freeze, a block): held while any owner is in the set. Holding twice
/// needs one release, and releasing an owner that holds nothing, or null, changes nothing.
/// </summary>
public sealed class HoldSet
{
    private readonly HashSet<object> owners = new();

    public bool IsHeld => owners.Count > 0;

    /// <summary>Adds <paramref name="owner"/>; returns whether the set changed.</summary>
    public bool Hold(object owner)
    {
        return owner != null && owners.Add(owner);
    }

    /// <summary>Removes <paramref name="owner"/>; returns whether the set changed.</summary>
    public bool Release(object owner)
    {
        return owner != null && owners.Remove(owner);
    }

    /// <summary>
    /// Releases every owner at once, leaving the set not held.
    /// </summary>
    public void Clear()
    {
        owners.Clear();
    }
}
