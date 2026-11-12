/// <summary>
/// Leaving the game from the in-game menu: back to the main menu, or out of the application, so the menu's
/// confirmations are tested without ending a session.
/// </summary>
public interface IGameExit
{
    /// <summary>
    /// Returns to the main menu, which ends an online session.
    /// </summary>
    void LeaveToMenu();

    /// <summary>
    /// Closes the application (in the editor, leaves Play Mode).
    /// </summary>
    void Quit();
}
