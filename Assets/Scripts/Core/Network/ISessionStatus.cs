/// <summary>
/// Whether this machine is in an online session and whether it hosts it, for behaviour that differs between solo
/// and online play (pausing, warning a host that leaving ends the game).
/// </summary>
public interface ISessionStatus
{
    bool IsOnline { get; }
    bool IsHost { get; }
}
