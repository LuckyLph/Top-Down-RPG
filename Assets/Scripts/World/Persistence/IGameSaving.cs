/// <summary>
/// Saving on request, for the in-game menu's Save game button and its confirmations (Docs/UiRevamp.md, Phase 6), so
/// the menu is tested without a world session.
/// </summary>
public interface IGameSaving
{
    /// <summary>
    /// Whether this machine writes the game now: it decides game state (offline or the host), a world session runs and
    /// that session is no throwaway.
    /// </summary>
    bool CanSave { get; }

    /// <summary>
    /// Whether the last write of this machine's save failed: the game's own offline or on the host, a joined client's
    /// copy of its place.
    /// </summary>
    bool LastWriteFailed { get; }

    /// <summary>
    /// Writes the game now; false when it could not, or this machine does not write it (<see cref="CanSave"/>).
    /// </summary>
    bool Save();
}
