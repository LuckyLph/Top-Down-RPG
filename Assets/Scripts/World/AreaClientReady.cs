using VContainer.Unity;

/// <summary>
/// Area-scope entry point that, on a connected client, tells the host this machine has finished loading the area
/// once the area scope starts. Does nothing offline or on the host.
/// </summary>
public sealed class AreaClientReady : IStartable
{
    private readonly NetworkSession session;

    /// <summary>
    /// Captures the session used to report readiness.
    /// </summary>
    public AreaClientReady(NetworkSession session)
    {
        this.session = session;
    }

    /// <summary>
    /// Sends the ready report for the current area epoch when this machine is a connected client.
    /// </summary>
    public void Start()
    {
        if (session.IsConnectedClient && !session.IsServer)
        {
            session.NotifyReady();
        }
    }
}
