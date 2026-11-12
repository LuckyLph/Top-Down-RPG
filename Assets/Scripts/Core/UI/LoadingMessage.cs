using System;
using VContainer.Unity;

/// <summary>
/// The line shown over the fader while a transition loads (Docs/UiRevamp.md, Phase 9), such as "Joining
/// 100.101.102.103's game…" while a joined client waits for the host to place its player. Set by whoever starts the
/// transition and cleared as every transition finishes; the Gameplay assembly's <c>LoadingLine</c> draws it, so Core
/// stays free of TextMesh Pro. Main-scope entry point, on this machine only.
/// </summary>
public sealed class LoadingMessage : IStartable, IDisposable
{
    private readonly IGameFlow gameFlow;

    /// <summary>
    /// Creates the message, cleared as each of <paramref name="gameFlow"/>'s transitions finishes.
    /// </summary>
    public LoadingMessage(IGameFlow gameFlow)
    {
        this.gameFlow = gameFlow;
    }

    /// <summary>
    /// Raised when <see cref="Text"/> changes.
    /// </summary>
    public event Action Changed;

    /// <summary>
    /// The line to show, or empty for none.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>
    /// Follows the game flow's transitions.
    /// </summary>
    public void Start()
    {
        gameFlow.TransitionFinished += Clear;
    }

    /// <summary>
    /// Stops following the game flow.
    /// </summary>
    public void Dispose()
    {
        gameFlow.TransitionFinished -= Clear;
    }

    /// <summary>
    /// Shows <paramref name="text"/> until the transition finishes.
    /// </summary>
    public void Show(string text)
    {
        string shown = text ?? string.Empty;
        if (shown == Text)
        {
            return;
        }

        Text = shown;
        Changed?.Invoke();
    }

    /// <summary>
    /// Shows nothing.
    /// </summary>
    public void Clear()
    {
        Show(string.Empty);
    }
}
