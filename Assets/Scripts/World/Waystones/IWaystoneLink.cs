/// <summary>
/// How the host tells a client's machine that its player discovered a waystone, so it can show it. Implemented by the
/// player's <see cref="PlayerWorldSync"/>; tests use a fake.
/// </summary>
public interface IWaystoneLink
{
    /// <summary>
    /// Host only: tells the owner of this player, when it is a client, that it discovered the waystone
    /// <paramref name="entryName"/>. Nothing for the host's own player or offline.
    /// </summary>
    void SendWaystoneDiscovered(string entryName);
}
