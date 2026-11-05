using System;

/// <summary>
/// An <see cref="IWallClock"/> tests set by hand, starting on 2026-11-04 at noon UTC.
/// </summary>
public sealed class ManualWallClock : IWallClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Moves the clock on by <paramref name="seconds"/>.
    /// </summary>
    public void Advance(double seconds)
    {
        UtcNow = UtcNow.AddSeconds(seconds);
    }
}
