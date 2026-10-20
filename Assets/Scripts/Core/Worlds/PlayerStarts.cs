using UnityEngine;

/// <summary>
/// Where a player starts a session (Docs/WorldStreaming.md: Player identity): its saved world and position when the
/// <see cref="SessionSave"/> knows its player id, otherwise the starting world's entry from <see cref="GameScenes"/>.
/// The menu asks it for the local player as Continue or Host Game starts, and the host for each player that joins.
/// </summary>
public sealed class PlayerStarts
{
    private readonly GameScenes gameScenes;
    private readonly SessionSave sessionSave;

    /// <summary>
    /// Creates the rule over <paramref name="scenes"/>' worlds and starting entry and <paramref name="save"/>'s places.
    /// </summary>
    public PlayerStarts(GameScenes scenes, SessionSave save)
    {
        gameScenes = scenes;
        sessionSave = save;
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
            Debug.LogError($"{gameScenes.name} has no starting world; a player has nowhere to start.", gameScenes);
            position = default;
            return false;
        }

        position = world.EntryPosition(gameScenes.StartingWorldEntry);
        return true;
    }

    /// <summary>
    /// The saved world and position of <paramref name="playerId"/>. False when the session does not know the player,
    /// and false with a warning when its saved world is not one this build lists or its position is on none of that
    /// world's chunks (the world changed since it was saved).
    /// </summary>
    private bool TryGetSaved(string playerId, out WorldDefinition world, out Vector2 position)
    {
        world = null;
        if (!sessionSave.TryGetPlace(playerId, out string worldId, out position))
        {
            return false;
        }

        WorldDefinition saved = gameScenes.FindWorldById(worldId);
        if (saved == null)
        {
            Debug.LogWarning($"Player {playerId} was saved in the world {worldId}, which this build does not list; it starts at the starting entry.");
            return false;
        }

        if (!saved.HasChunkAt(position))
        {
            Debug.LogWarning($"Player {playerId} was saved at {position} in {saved.name}, on none of its chunks; it starts at the starting entry.", saved);
            return false;
        }

        world = saved;
        return true;
    }
}
