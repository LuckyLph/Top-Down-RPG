using VContainer.Unity;

public sealed class AreaClientReady : IStartable
{
    private readonly NetworkSession session;

    public AreaClientReady(NetworkSession session)
    {
        this.session = session;
    }

    public void Start()
    {
        if (session.IsConnectedClient && !session.IsServer)
        {
            session.NotifyReady();
        }
    }
}
