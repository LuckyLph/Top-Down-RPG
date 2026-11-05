using System;

/// <summary>
/// The machine's own clock, in UTC.
/// </summary>
public sealed class SystemWallClock : IWallClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
