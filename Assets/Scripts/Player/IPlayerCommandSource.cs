/// <summary>
/// Where a <see cref="PlayerController"/> gets its intent from; the seam that lets tests feed commands instead of
/// the local input.
/// </summary>
public interface IPlayerCommandSource
{
    /// <summary>
    /// Reads this frame's command. Called once per frame by a player that simulates its own movement.
    /// </summary>
    PlayerCommand ReadCommand();
}
