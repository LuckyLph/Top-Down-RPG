using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay-scope check of the place a player starts at (Docs/WorldGeneration.md: Runtime changes), run offline and on
/// the host once the chunks around the place are up: a place on a cell its world's navigation lets a player stand on
/// stays, and one on a cell it does not (the ground there changed since it was saved: water, a wall, the face of raised
/// ground) moves to the centre of the nearest walkable cell within <see cref="GameplaySettings.SavedPlaceSearchCells"/>,
/// or else to the player's nearest discovered waystone of that world (the default entry counting), with one warning.
/// A place whose chunk has no navigation page loaded is kept, since nothing can tell.
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
                Debug.LogWarning($"{world.name}: player {playerId}'s place {world.ToLocal(place)} (world-local) is not walkable ground any more; it starts at {world.ToLocal(moved)}, the nearest walkable cell.", world);
                return true;
            case Outcome.Waystone:
                Debug.LogWarning($"{world.name}: player {playerId}'s place {world.ToLocal(place)} (world-local) is not walkable ground any more and no walkable cell lies within {searchCells} cells; it starts at {world.ToLocal(moved)}, its nearest discovered waystone {waystone.Name}.", world);
                return true;
            case Outcome.Nowhere:
                Debug.LogWarning($"{world.name}: player {playerId}'s place {world.ToLocal(place)} (world-local) is not walkable ground any more, no walkable cell lies within {searchCells} cells and the world has no entry; it starts there.", world);
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// The rule itself, on any <paramref name="navigation"/>: <see cref="Outcome.Walkable"/> when
    /// <paramref name="profile"/> can stand on the cell of <paramref name="place"/>; else
    /// <see cref="Outcome.NearestCell"/>, with <paramref name="moved"/> the centre of the walkable cell nearest it by
    /// Manhattan distance up to <paramref name="searchCells"/> (<see cref="INavigation2D.TryGetNearestWalkableCell"/>);
    /// else <see cref="Outcome.Waystone"/>, with <paramref name="moved"/> the position of <paramref name="world"/>'s
    /// entry nearest the place in a straight line among those <paramref name="discovered"/> counts as discovered
    /// (<see cref="WaystoneRules.TryFindNearest"/>, the default entry always), given in <paramref name="waystone"/>; else
    /// <see cref="Outcome.Nowhere"/>, the place kept.
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
        if (navigation.IsCellWalkable(cell, profile))
        {
            return Outcome.Walkable;
        }

        if (searchCells > 0 && navigation.TryGetNearestWalkableCell(cell, profile, out Vector3Int nearest, searchCells))
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
}
