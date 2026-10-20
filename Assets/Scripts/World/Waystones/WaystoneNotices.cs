using System;

/// <summary>
/// Gameplay-scope news of waystone discoveries on this machine, for what shows them: offline and on the host every
/// player's, raised by <see cref="WaystoneDiscovery"/>; on a client its own player's, raised by its
/// <see cref="PlayerWorldSync"/> when the host tells it.
/// </summary>
public sealed class WaystoneNotices
{
    /// <summary>
    /// Raised with the player and the name of the waystone entry it discovered.
    /// </summary>
    public event Action<PlayerHandle, string> Discovered;

    /// <summary>
    /// Tells listeners that <paramref name="player"/> discovered the waystone <paramref name="entryName"/>.
    /// </summary>
    public void Raise(PlayerHandle player, string entryName)
    {
        if (player != null)
        {
            Discovered?.Invoke(player, entryName);
        }
    }
}
