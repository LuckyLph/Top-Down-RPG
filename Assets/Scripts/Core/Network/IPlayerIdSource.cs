/// <summary>
/// Where player ids come from (Docs/WorldStreaming.md: Player identity): this install's own, and on the host the one
/// each client joined with. Implemented by <see cref="NetworkSession"/>; tests use a fake.
/// </summary>
public interface IPlayerIdSource
{
    /// <summary>
    /// This install's player id.
    /// </summary>
    string LocalPlayerId { get; }

    /// <summary>
    /// The player id <paramref name="clientId"/> joined with; this install's own for the host's client, empty for a
    /// client the host does not know.
    /// </summary>
    string PlayerIdOf(ulong clientId);
}
