/// <summary>
/// Whether a connected client has finished loading the current area. The host's own client always is.
/// </summary>
public interface IClientReadiness
{
    /// <summary>
    /// Whether client <paramref name="clientId"/> reported ready for the current area; always true for the host's
    /// own client.
    /// </summary>
    bool IsClientReady(ulong clientId);
}
