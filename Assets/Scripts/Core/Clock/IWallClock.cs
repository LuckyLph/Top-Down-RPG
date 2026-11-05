using System;

/// <summary>
/// The time of day in the real world, for what is stamped with when it happened (a save's <c>savedAt</c>), so tests
/// can substitute a fixed one. Game logic runs on <see cref="IClock"/>.
/// </summary>
public interface IWallClock
{
    DateTime UtcNow { get; }
}
