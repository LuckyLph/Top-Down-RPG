using System;
using VContainer.Unity;

/// <summary>
/// Stops game time while a <see cref="GameFlow"/> transition runs: it holds the <see cref="TimeFreeze"/> from
/// <see cref="GameFlow.TransitionStarted"/> to <see cref="GameFlow.TransitionFinished"/>, so nothing ticks, moves
/// or expires behind the fade. A trip between worlds (<see cref="GameFlow.IsTravelling"/>) is a local fade only and
/// never stops time. The fader runs on unscaled time.
/// </summary>
public sealed class TransitionTimeFreeze : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;
    private readonly TimeFreeze timeFreeze;

    /// <summary>
    /// Creates the freeze that follows <paramref name="gameFlow"/>'s transitions through <paramref name="timeFreeze"/>.
    /// </summary>
    public TransitionTimeFreeze(GameFlow gameFlow, TimeFreeze timeFreeze)
    {
        this.gameFlow = gameFlow;
        this.timeFreeze = timeFreeze;
    }

    /// <summary>
    /// Subscribes to transition start and end and applies the current state right away.
    /// </summary>
    public void Start()
    {
        gameFlow.TransitionStarted += Refresh;
        gameFlow.TransitionFinished += Refresh;
        Refresh();
    }

    /// <summary>
    /// Unsubscribes and releases its hold, so a transition cut short by shutdown never leaves time frozen.
    /// </summary>
    public void Dispose()
    {
        gameFlow.TransitionStarted -= Refresh;
        gameFlow.TransitionFinished -= Refresh;
        timeFreeze.Release(this);
    }

    /// <summary>
    /// Whether game time stops: while a transition runs (<paramref name="transitioning"/>) that is not a trip between
    /// worlds (<paramref name="travelling"/>).
    /// </summary>
    internal static bool Freezes(bool transitioning, bool travelling)
    {
        return transitioning && !travelling;
    }

    /// <summary>
    /// Holds the freeze while a transition that stops time runs and releases it otherwise.
    /// </summary>
    private void Refresh()
    {
        if (Freezes(gameFlow.IsTransitioning, gameFlow.IsTravelling))
        {
            timeFreeze.Hold(this);
        }
        else
        {
            timeFreeze.Release(this);
        }
    }
}
