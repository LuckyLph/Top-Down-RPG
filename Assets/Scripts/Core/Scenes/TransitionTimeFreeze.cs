using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Stops game time while a <see cref="GameFlow"/> transition runs: <see cref="Time.timeScale"/> is 0 from
/// <see cref="GameFlow.TransitionStarted"/> to <see cref="GameFlow.TransitionFinished"/>, so nothing ticks, moves
/// or expires behind the fade. The fader runs on unscaled time.
/// </summary>
public sealed class TransitionTimeFreeze : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;

    public TransitionTimeFreeze(GameFlow gameFlow)
    {
        this.gameFlow = gameFlow;
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
        Time.timeScale = 1f;
    }

    private void Refresh()
    {
        Time.timeScale = gameFlow.IsTransitioning ? 0f : 1f;
    }
}
