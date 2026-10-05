using System;
using VContainer.Unity;

/// <summary>
/// Stops game time while a <see cref="GameFlow"/> transition runs: it holds the <see cref="TimeFreeze"/> from
/// <see cref="GameFlow.TransitionStarted"/> to <see cref="GameFlow.TransitionFinished"/>, so nothing ticks, moves
/// or expires behind the fade. The fader runs on unscaled time.
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
    /// Holds the freeze while a transition runs and releases it otherwise.
    /// </summary>
    private void Refresh()
    {
        if (gameFlow.IsTransitioning)
        {
            timeFreeze.Hold(this);
        }
        else
        {
            timeFreeze.Release(this);
        }
    }
}
