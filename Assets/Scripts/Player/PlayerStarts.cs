using UnityEngine;

/// <summary>
/// Where the host places a player as it joins (Docs/WorldStreaming.md: Player identity): its saved world and position
/// when the host knows its player id, otherwise the starting world's entry from <see cref="GameScenes"/>. Nothing is
/// saved per player yet, so every player starts at the starting world's entry.
/// </summary>
public sealed class PlayerStarts
{
    private readonly GameScenes gameScenes;

    /// <summary>
    /// Creates the rule over <paramref name="scenes"/>' starting world and entry.
    /// </summary>
    public PlayerStarts(GameScenes scenes)
    {
        gameScenes = scenes;
    }

    /// <summary>
    /// The world and world position the player <paramref name="playerId"/> starts at: where it was saved, else the
    /// starting world's entry (its default entry, with a warning, for an unknown name). False, with an error, when no
    /// starting world is set.
    /// </summary>
    public bool TryGetStart(string playerId, out WorldDefinition world, out Vector2 position)
    {
        if (TryGetSaved(playerId, out world, out position))
        {
            return true;
        }

        world = gameScenes.StartingWorld;
        if (world == null)
        {
            Debug.LogError($"{gameScenes.name} has no starting world; a joining player has nowhere to start.", gameScenes);
            position = default;
            return false;
        }

        WorldDefinition.Entry entry = world.FindEntry(gameScenes.StartingWorldEntry);
        position = world.ToWorld(entry != null ? entry.Position : world.LocalBounds.center);
        return true;
    }

    /// <summary>
    /// The saved world and position of <paramref name="playerId"/>; false while saves keep no player's place, until the
    /// save keys each player's world and position by its id (Docs/WorldStreaming.md: Persistence, Phase 8).
    /// </summary>
    private bool TryGetSaved(string playerId, out WorldDefinition world, out Vector2 position)
    {
        world = null;
        position = default;
        return false;
    }
}
