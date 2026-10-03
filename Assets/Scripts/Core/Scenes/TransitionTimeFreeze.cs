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

    public TransitionTimeFreeze(GameFlow gameFlow, TimeFreeze timeFreeze)
    {
        this.gameFlow = gameFlow;
        this.timeFreeze = timeFreeze;
    }

    public void Start()
    {
        gameFlow.TransitionStarted += Refresh;
        gameFlow.TransitionFinished += Refresh;
        Refresh();
    }

    public void Dispose()
    {
        gameFlow.TransitionStarted -= Refresh;
        gameFlow.TransitionFinished -= Refresh;
        timeFreeze.Release(this);
    }

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
