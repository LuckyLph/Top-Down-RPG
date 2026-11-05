using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay-scope check of the place a player starts at (Docs/WorldGeneration.md: Runtime changes), run offline and on
/// the host once the chunks around the place are up: a place on a cell its world's navigation lets a player stand on and
/// leave for the rest of the world stays, and one on a cell it does not (the ground there changed since it was saved:
/// water, a wall, the face of raised ground, or ground cut off from every entry of the world, Docs/KnownGaps.md, Phase
/// 5) moves to the centre of the nearest walkable cell reaching the world's entries within
/// <see cref="GameplaySettings.SavedPlaceSearchCells"/> (<see cref="IsReachable"/>), or else to the player's nearest
/// discovered waystone of that world (the default entry counting), with one warning. A place whose chunk has no
/// navigation page loaded is kept, since nothing can tell.
/// </summary>
public sealed class WalkablePlaces
{
    /// <summary>
    /// What the check found for a place.
    /// </summary>
    internal enum Outcome
    {
        Walkable,
        NearestCell,
        Waystone,
        Nowhere,
    }

    private readonly Worlds worlds;
    private readonly SessionSave sessionSave;
    private readonly TerrainMovementProfile2D movementProfile;
    private readonly int searchCells;

    /// <summary>
    /// Creates the check over the loaded <paramref name="loadedWorlds"/>' navigation, the players' discoveries in
    /// <paramref name="save"/>, the players' movement profile from <paramref name="controlSettings"/> and the search
    /// radius from <paramref name="settings"/>.
    /// </summary>
    public WalkablePlaces(Worlds loadedWorlds, SessionSave save, PlayerControlSettings controlSettings, GameplaySettings settings)
    {
        worlds = loadedWorlds;
        sessionSave = save;
        movementProfile = controlSettings != null ? controlSettings.MovementProfile : null;
        searchCells = settings != null ? settings.SavedPlaceSearchCells : 1;
    }

    /// <summary>
    /// Where the player <paramref name="playerId"/> starts instead of <paramref name="place"/> in
    /// <paramref name="world"/>: true, with the new place in <paramref name="moved"/> and a warning naming the world and
    /// both places, when the place is not walkable; false, with <paramref name="moved"/> the place itself, when it is,
    /// when the world or the place's chunk is not loaded, or when there is nowhere to send the player (a warning then).
    /// </summary>
    public bool TryMove(WorldDefinition world, Vector2 place, string playerId, out Vector2 moved)
    {
        moved = place;
        if (world == null || !worlds.TryGet(world, out LoadedWorld loaded) || loaded.Navigation == null
            || !loaded.Navigation.HasChunk(ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(place)))))
        {
            return false;
        }

        Outcome outcome = Find(loaded.Navigation, movementProfile, searchCells, world, place, sessionSave.WaystonesOf(playerId), out moved, out WorldDefinition.Entry waystone);
        switch (outcome)
        {
            case Outcome.NearestCell:
                Debug.LogWarning($"{world.name}: player {playerId}'s place {world.ToLocal(place)} (world-local) {Problem(loaded.Navigation, world, place)}; it starts at {world.ToLocal(moved)}, the nearest walkable cell reaching the world's entries.", world);
                return true;
            case Outcome.Waystone:
                Debug.LogWarning($"{world.name}: player {playerId}'s place {world.ToLocal(place)} (world-local) {Problem(loaded.Navigation, world, place)} and no walkable cell reaching the world's entries lies within {searchCells} cells; it starts at {world.ToLocal(moved)}, its nearest discovered waystone {waystone.Name}.", world);
                return true;
            case Outcome.Nowhere:
                Debug.LogWarning($"{world.name}: player {playerId}'s place {world.ToLocal(place)} (world-local) {Problem(loaded.Navigation, world, place)}, no walkable cell reaching the world's entries lies within {searchCells} cells and the world has no entry; it starts there.", world);
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// The rule itself, on any <paramref name="navigation"/>: <see cref="Outcome.Walkable"/> when
    /// <paramref name="profile"/> can stand on the cell of <paramref name="place"/> and reach the rest of the world from
    /// it (<see cref="IsReachable"/>); else <see cref="Outcome.NearestCell"/>, with <paramref name="moved"/> the centre
    /// of the nearest such cell by Manhattan distance up to <paramref name="searchCells"/>, searched in rings as
    /// <see cref="INavigation2D.TryGetNearestWalkableCell"/> searches; else <see cref="Outcome.Waystone"/>, with
    /// <paramref name="moved"/> the position of <paramref name="world"/>'s entry nearest the place in a straight line
    /// among those <paramref name="discovered"/> counts as discovered (<see cref="WaystoneRules.TryFindNearest"/>, the
    /// default entry always), given in <paramref name="waystone"/>; else <see cref="Outcome.Nowhere"/>, the place kept.
    /// </summary>
    internal static Outcome Find(
        INavigation2D navigation,
        TerrainMovementProfile2D profile,
        int searchCells,
        WorldDefinition world,
        Vector2 place,
        IReadOnlyList<GameSaveData.Waystone> discovered,
        out Vector2 moved,
        out WorldDefinition.Entry waystone)
    {
        moved = place;
        waystone = null;
        Vector3Int cell = navigation.WorldToCell(place);
        List<Vector3Int> pockets = new();
        if (IsReachable(navigation, profile, world, cell, pockets))
        {
            return Outcome.Walkable;
        }

        if (TryFindNearestReachable(navigation, profile, world, cell, searchCells, pockets, out Vector3Int nearest))
        {
            moved = navigation.CellToWorldCenter(nearest);
            return Outcome.NearestCell;
        }

        if (WaystoneRules.TryFindNearest(world, place, discovered, out waystone))
        {
            moved = world.ToWorld(waystone.Position);
            return Outcome.Waystone;
        }

        return Outcome.Nowhere;
    }

    /// <summary>
    /// What is wrong with <paramref name="place"/> in <paramref name="world"/>, for the warning: not walkable any more,
    /// or walkable but cut off from the world's entries.
    /// </summary>
    private string Problem(INavigation2D navigation, WorldDefinition world, Vector2 place)
    {
        return navigation.IsCellWalkable(navigation.WorldToCell(place), movementProfile)
            ? "is cut off from the world's entries (an islet, a plateau with no ramp, a walled yard)"
            : "is not walkable ground any more";
    }

    /// <summary>
    /// Whether a player can stand on <paramref name="cell"/> and get from it to the rest of <paramref name="world"/>:
    /// the cell is walkable for <paramref name="profile"/> and its region either holds one of the world's entries (its
    /// default, its waystones and gates, every place players arrive at) or runs on beyond the loaded ground
    /// (<see cref="INavigation2D.RunsBeyondLoadedPages"/>). A region doing neither lies wholly in the loaded ground and
    /// reaches no entry: a pocket (an islet in a lake, a plateau with no ramp, a walled yard), remembered in
    /// <paramref name="pockets"/> so the cells of one pocket are checked once. A pocket larger than the loaded ground
    /// cannot be told from the rest of the world, and counts as reachable; so does every walkable cell of a world with
    /// no entry, which has nothing to reach.
    /// </summary>
    internal static bool IsReachable(INavigation2D navigation, TerrainMovementProfile2D profile, WorldDefinition world, Vector3Int cell, List<Vector3Int> pockets)
    {
        if (!navigation.IsCellWalkable(cell, profile))
        {
            return false;
        }

        if (world == null || world.Entries.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < pockets.Count; i++)
        {
            if (navigation.AreCellsConnected(cell, pockets[i], profile))
            {
                return false;
            }
        }

        if (navigation.RunsBeyondLoadedPages(cell, profile))
        {
            return true;
        }

        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            if (entry != null && navigation.AreCellsConnected(cell, navigation.WorldToCell(world.ToWorld(entry.Position)), profile))
            {
                return true;
            }
        }

        pockets.Add(cell);
        return false;
    }

    /// <summary>
    /// The reachable cell (<see cref="IsReachable"/>) nearest <paramref name="origin"/> by Manhattan distance, up to
    /// <paramref name="searchCells"/>, in rings of growing distance, each ring from its left end, the upper cell of a
    /// column first. False when none lies within reach.
    /// </summary>
    private static bool TryFindNearestReachable(INavigation2D navigation, TerrainMovementProfile2D profile, WorldDefinition world, Vector3Int origin, int searchCells, List<Vector3Int> pockets, out Vector3Int nearest)
    {
        for (int radius = 1; radius <= searchCells; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int dy = radius - Mathf.Abs(dx);
                Vector3Int top = new(origin.x + dx, origin.y + dy, origin.z);
                if (IsReachable(navigation, profile, world, top, pockets))
                {
                    nearest = top;
                    return true;
                }

                Vector3Int bottom = new(origin.x + dx, origin.y - dy, origin.z);
                if (dy != 0 && IsReachable(navigation, profile, world, bottom, pockets))
                {
                    nearest = bottom;
                    return true;
                }
            }
        }

        nearest = origin;
        return false;
    }
}
