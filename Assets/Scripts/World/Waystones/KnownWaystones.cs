using System;
using System.Collections.Generic;

/// <summary>
/// Gameplay-scope record of the waystones this machine's own player has discovered (Docs/KnownGaps.md, Phase 3), which
/// the waystones' markers show: where game state is decided (offline, the host) the <see cref="SessionSave"/>'s under
/// this install's player id; on a client the list the host replicates on its player (<see cref="PlayerWorldSync"/>),
/// filled from the host's save as the player spawns and grown with each discovery, handed in through
/// <see cref="Replicate"/>. A world's default entry counts as discovered from the start (<see cref="WaystoneRules"/>).
/// <see cref="Changed"/> tells the markers to look again: on a discovery where game state is decided, on each
/// replicated change on a client.
/// </summary>
public sealed class KnownWaystones : IDisposable
{
    private readonly IGameAuthority authority;
    private readonly IPlayerIdSource ids;
    private readonly SessionSave sessionSave;
    private readonly WaystoneNotices notices;
    private readonly List<GameSaveData.Waystone> replicated = new();

    /// <summary>
    /// Reads the discoveries of <paramref name="playerIds"/>'s local player in <paramref name="save"/> while
    /// <paramref name="gameAuthority"/> decides game state, following <paramref name="discoveries"/>.
    /// </summary>
    public KnownWaystones(IGameAuthority gameAuthority, IPlayerIdSource playerIds, SessionSave save, WaystoneNotices discoveries)
    {
        authority = gameAuthority;
        ids = playerIds;
        sessionSave = save;
        notices = discoveries;
        notices.Discovered += HandleDiscovered;
    }

    /// <summary>
    /// Raised when what this machine's player discovered may have changed.
    /// </summary>
    public event Action Changed;

    /// <summary>
    /// The waystones this machine's player discovered: the session's own where game state is decided, the host's
    /// replicated list on a client.
    /// </summary>
    public IReadOnlyList<GameSaveData.Waystone> Discovered => authority.IsAuthoritative ? sessionSave.WaystonesOf(ids.LocalPlayerId) : replicated;

    /// <summary>
    /// Whether this machine's player discovered <paramref name="entry"/> of <paramref name="world"/>
    /// (<see cref="WaystoneRules.IsDiscovered"/>).
    /// </summary>
    public bool IsDiscovered(WorldDefinition world, WorldDefinition.Entry entry)
    {
        return WaystoneRules.IsDiscovered(world, entry, Discovered);
    }

    /// <summary>
    /// A client's own player's discoveries as the host replicated them, each once, and raises <see cref="Changed"/>.
    /// </summary>
    public void Replicate(IReadOnlyList<GameSaveData.Waystone> discovered)
    {
        replicated.Clear();
        for (int i = 0; discovered != null && i < discovered.Count; i++)
        {
            GameSaveData.Waystone waystone = discovered[i];
            if (waystone != null && !WaystoneRules.Contains(replicated, waystone.WorldId, waystone.Entry))
            {
                replicated.Add(waystone);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Stops following discoveries.
    /// </summary>
    public void Dispose()
    {
        notices.Discovered -= HandleDiscovered;
    }

    /// <summary>
    /// Where game state is decided, a discovery is already in the session's save: the markers look again.
    /// </summary>
    private void HandleDiscovered(PlayerHandle player, string entryName)
    {
        if (authority.IsAuthoritative)
        {
            Changed?.Invoke();
        }
    }
}
