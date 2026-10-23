using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The trip's checks, offline and on the host (Docs/WorldStreaming.md: Travelling between worlds); in play
/// <see cref="NetworkTripAuthority"/> sends requests here, from this machine or over the network. For each request it
/// checks that the player is alive (its machine may not know yet that it died), that the exit is loaded in the player's
/// world, that the player stands at it (within <see cref="ReachCells"/>
/// of its cells, where the request says its machine has it, or else where its body is) and that it leads to a listed
/// world with an entry (an unknown entry name falls back to the default entry, with a warning). Then it prepares the
/// trip, holding the arrival in <see cref="TripArrivals"/> so the target world and the chunks around the entry start
/// loading, puts the player in the target world for one more trip (<see cref="PlayerWorlds.Travel"/>), and raises
/// <see cref="Approved"/> for the player's machine to go. A player whose trip is under way cannot ask for another until
/// it arrives. The authority also starts trips of its own (<see cref="Send"/>): a dead player's to the waystone it
/// respawns at, within its world.
/// </summary>
public sealed class LocalTripAuthority : ITripAuthority
{
    public const int ReachCells = 1;

    private readonly WorldExits exits;
    private readonly PlayerWorlds playerWorlds;
    private readonly TripArrivals arrivals;
    private readonly IReadOnlyList<WorldDefinition> listedWorlds;

    /// <summary>
    /// Creates the authority over the loaded <paramref name="worldExits"/>, the players' worlds and the trips under
    /// way, letting players travel only to the worlds <paramref name="gameScenes"/> lists.
    /// </summary>
    public LocalTripAuthority(WorldExits worldExits, PlayerWorlds worldsOfPlayers, TripArrivals tripArrivals, GameScenes gameScenes)
    {
        exits = worldExits;
        playerWorlds = worldsOfPlayers;
        arrivals = tripArrivals;
        listedWorlds = gameScenes.Worlds;
    }

    public event Action<TripApproval> Approved;

    /// <summary>
    /// Checks and carries out <paramref name="request"/>, raising <see cref="Approved"/> when it goes and logging a
    /// warning with the reason when it does not.
    /// </summary>
    public void Request(TripRequest request)
    {
        if (TryApprove(request, out TripApproval approval, out string refusal))
        {
            Approved?.Invoke(approval);
        }
        else
        {
            Debug.LogWarning($"Trip refused: {refusal}");
        }
    }

    /// <summary>
    /// Checks <paramref name="request"/> and, when it is allowed, prepares the trip and puts the player in the target
    /// world, giving the <paramref name="approval"/>; otherwise changes nothing and gives the
    /// <paramref name="refusal"/>.
    /// </summary>
    internal bool TryApprove(TripRequest request, out TripApproval approval, out string refusal)
    {
        approval = default;
        PlayerHandle player = request.Player;
        if (player == null || player.Transform == null)
        {
            refusal = "the player is gone.";
            return false;
        }

        if (player.Health != null && player.Health.IsDead)
        {
            refusal = "the player is dead; it travels only to the waystone it respawns at.";
            return false;
        }

        WorldDefinition world = playerWorlds.Get(player);
        if (world == null)
        {
            refusal = "the player is in no world.";
            return false;
        }

        if (arrivals.IsHeld(player))
        {
            refusal = "the player is already travelling.";
            return false;
        }

        if (!exits.TryFind(world, request.ExitId, out WorldExit exit))
        {
            refusal = $"no exit '{request.ExitId}' is loaded in {world.name}, the player's world.";
            return false;
        }

        RectInt reach = exit.CellsIn(world);
        reach = new RectInt(reach.position - Vector2Int.one * ReachCells, reach.size + Vector2Int.one * (2 * ReachCells));
        Vector2 standing = request.HasPosition ? request.Position : (Vector2)player.Transform.position;
        Vector2Int cell = ChunkCoords.CellOf(world.ToLocal(standing));
        if (!reach.Contains(cell))
        {
            refusal = $"the player, on cell {cell} of {world.name}, is not at the exit {exit.name}.";
            return false;
        }

        WorldDefinition target = exit.TargetWorld;
        if (target == null || !IsListed(target))
        {
            refusal = $"the exit {exit.name} leads to {(target != null ? target.name : "no world")}, which Game Scenes does not list.";
            return false;
        }

        WorldDefinition.Entry entry = target.FindEntry(exit.TargetEntry);
        if (entry == null)
        {
            refusal = $"{target.name}, where the exit {exit.name} leads, has no entry.";
            return false;
        }

        approval = Send(player, target, entry.Name);
        refusal = null;
        return true;
    }

    /// <summary>
    /// Sends <paramref name="player"/> on a trip to <paramref name="world"/>'s entry <paramref name="entryName"/> (the
    /// default entry, with a warning, for an unknown name; the world's middle when it has no entry), checking nothing:
    /// holds the arrival so the world and the chunks around the entry start loading, puts the player in the world for
    /// one more trip, and returns the approval its machine goes by. A trip approved from a request goes this way, and so
    /// does a respawn, which needs no exit.
    /// </summary>
    internal TripApproval Send(PlayerHandle player, WorldDefinition world, string entryName)
    {
        WorldDefinition.Entry entry = world.FindEntry(entryName);
        string resolved = entry != null ? entry.Name : string.Empty;
        Vector2 position = world.EntryPosition(resolved);
        arrivals.Hold(player, new StreamInterest(world, position));
        playerWorlds.Travel(player, world, resolved);
        return new TripApproval(player, world, resolved, position);
    }

    private bool IsListed(WorldDefinition world)
    {
        for (int i = 0; i < listedWorlds.Count; i++)
        {
            if (listedWorlds[i] == world)
            {
                return true;
            }
        }

        return false;
    }
}
