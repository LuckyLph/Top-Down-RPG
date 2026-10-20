/// <summary>
/// Keeps a world exit from firing again and again: it fires only when the player steps onto an exit from a cell that is
/// no exit. It starts unarmed, so a player that appears on an exit (a new session, a trip ending on one) must step off
/// first, and <see cref="Disarm"/> does the same after a trip. Plain logic, tested in EditMode.
/// </summary>
public sealed class ExitTriggerGuard
{
    private bool armed;

    public bool IsArmed => armed;

    /// <summary>
    /// Takes where the player now stands (<paramref name="onExit"/>) and whether that fires the exit: true once when it
    /// steps onto an exit while armed, which disarms the guard until it stands on no exit again.
    /// </summary>
    public bool Step(bool onExit)
    {
        if (!onExit)
        {
            armed = true;
            return false;
        }

        if (!armed)
        {
            return false;
        }

        armed = false;
        return true;
    }

    /// <summary>
    /// Waits until the player stands on no exit before any exit fires again.
    /// </summary>
    public void Disarm()
    {
        armed = false;
    }
}
