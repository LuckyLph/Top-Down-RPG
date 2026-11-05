/// <summary>
/// A joined client's tie to its host: whether this machine follows a host (joined, connected or not) and whether the
/// connection to it was lost without this machine leaving (the host left, stopped, crashed or timed out). Implemented
/// by <see cref="NetworkSession"/>; a client's copy of its own place reads it (Docs/KnownGaps.md, Phase 5).
/// </summary>
public interface IHostLink
{
    bool IsFollowingHost { get; }
    bool IsHostLost { get; }
}
