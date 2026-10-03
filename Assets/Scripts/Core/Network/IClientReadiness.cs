/// <summary>
/// Whether a connected client has finished loading the current area. The host's own client always is.
/// </summary>
public interface IClientReadiness
{
    bool IsClientReady(ulong clientId);
}
