/// <summary>
/// The session's shared clock: the host's network time in seconds, which every machine of a session reads alike (a
/// client's is its estimate of the host's), so a moment the host names by it means the same moment everywhere. Zero
/// outside a session.
/// </summary>
public interface INetworkClock
{
    double ServerTime { get; }
}
