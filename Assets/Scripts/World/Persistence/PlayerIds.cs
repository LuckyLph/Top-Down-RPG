using System;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Gameplay-scope record of each registered player's player id (Docs/WorldStreaming.md: Player identity), which keys
/// its place and discovered waystones in the <see cref="SessionSave"/>: its owner's on the host, this install's offline.
/// It learns a player's id as the player registers, while the player is still spawned, since its owner may be gone by
/// the time it leaves; so the id stays readable while the registry announces the player's removal, and is forgotten
/// once another player registers after it.
/// </summary>
public sealed class PlayerIds : IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly IPlayerIdSource source;
    private readonly ISessionStatus session;
    private readonly Dictionary<PlayerHandle, string> ids = new();
    private readonly List<PlayerHandle> gone = new();

    /// <summary>
    /// Learns the ids of the players in <paramref name="registry"/> now and as they register, from
    /// <paramref name="idSource"/> while <paramref name="sessionStatus"/> says a session runs.
    /// </summary>
    public PlayerIds(IPlayerRegistry registry, IPlayerIdSource idSource, ISessionStatus sessionStatus)
    {
        players = registry;
        source = idSource;
        session = sessionStatus;
        players.PlayerAdded += Learn;
        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            Learn(registered[i]);
        }
    }

    /// <summary>
    /// The player id of <paramref name="player"/>, as learned when it registered (looked up now for one not learned);
    /// empty when it has none.
    /// </summary>
    public string IdOf(PlayerHandle player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        if (ids.TryGetValue(player, out string id))
        {
            return id;
        }

        return Lookup(player) ?? string.Empty;
    }

    /// <summary>
    /// Stops learning.
    /// </summary>
    public void Dispose()
    {
        players.PlayerAdded -= Learn;
    }

    /// <summary>
    /// Forgets the players that left the registry, then learns <paramref name="player"/>'s id.
    /// </summary>
    private void Learn(PlayerHandle player)
    {
        if (player == null)
        {
            return;
        }

        gone.Clear();
        foreach (PlayerHandle known in ids.Keys)
        {
            if (!players.Contains(known))
            {
                gone.Add(known);
            }
        }

        for (int i = 0; i < gone.Count; i++)
        {
            ids.Remove(gone[i]);
        }

        ids[player] = Lookup(player) ?? string.Empty;
    }

    /// <summary>
    /// <paramref name="player"/>'s id now: its owner's while it is a spawned network object in a session, this
    /// install's otherwise.
    /// </summary>
    private string Lookup(PlayerHandle player)
    {
        if (session.IsActive && player.Transform != null
            && player.Transform.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
        {
            return source.PlayerIdOf(networkObject.OwnerClientId);
        }

        return source.LocalPlayerId;
    }
}
