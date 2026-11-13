/// <summary>
/// The game's own <see cref="IGameExit"/>: <see cref="GameFlow.ShowMainMenuAsync"/>, after which
/// <c>NetworkSessionLifecycle</c> ends an online session, and <see cref="ApplicationExit.Quit"/>.
/// </summary>
public sealed class GameExit : IGameExit
{
    private readonly GameFlow gameFlow;

    /// <summary>
    /// Leaves through <paramref name="gameFlow"/>.
    /// </summary>
    public GameExit(GameFlow gameFlow)
    {
        this.gameFlow = gameFlow;
    }

    /// <summary>
    /// Starts the return to the main menu.
    /// </summary>
    public void LeaveToMenu()
    {
        _ = gameFlow.ShowMainMenuAsync();
    }

    /// <summary>
    /// Quits the application.
    /// </summary>
    public void Quit()
    {
        ApplicationExit.Quit();
    }
}
