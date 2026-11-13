/// <summary>
/// <see cref="IGameExit"/> that only counts how often each way out was taken.
/// </summary>
public sealed class FakeGameExit : IGameExit
{
    public int Leaves { get; private set; }
    public int Quits { get; private set; }

    /// <summary>
    /// Counts a return to the main menu.
    /// </summary>
    public void LeaveToMenu()
    {
        Leaves++;
    }

    /// <summary>
    /// Counts a quit.
    /// </summary>
    public void Quit()
    {
        Quits++;
    }
}
