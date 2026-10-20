using System.Collections.Generic;

/// <summary>
/// An <see cref="IPlayerIdSource"/> for tests: this install's id and the ids of clients set by hand.
/// </summary>
public sealed class FakePlayerIdSource : IPlayerIdSource
{
    public Dictionary<ulong, string> Clients { get; } = new();

    public string LocalPlayerId { get; set; } = "local-player";

    /// <summary>
    /// The id set for <paramref name="clientId"/>, or empty.
    /// </summary>
    public string PlayerIdOf(ulong clientId)
    {
        return Clients.TryGetValue(clientId, out string id) ? id : string.Empty;
    }
}
