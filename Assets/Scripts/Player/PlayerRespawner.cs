/// <summary>
/// Brings a dead registered player back where it stands by restoring its health, which re-enables it through
/// <see cref="DisableOnDeath"/> and, in a session, reaches every machine through <see cref="NetworkHealth"/>.
/// </summary>
public sealed class PlayerRespawner
{
    private readonly IPlayerRegistry players;

    /// <summary>
    /// Captures the registry a respawned player must be in.
    /// </summary>
    public PlayerRespawner(IPlayerRegistry players)
    {
        this.players = players;
    }

    /// <summary>
    /// Restores <paramref name="player"/>'s health. False when the player is not a dead registered one.
    /// </summary>
    public bool TryRespawn(PlayerHandle player)
    {
        if (player == null || player.Transform == null || player.Health == null || !player.Health.IsDead || !players.Contains(player))
        {
            return false;
        }

        player.Health.Restore();
        return true;
    }
}
