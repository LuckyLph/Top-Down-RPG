using System;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point for the in-game menu's Controls page (Docs/UiRevamp.md, Phase 6), on this machine only:
/// shows the keys bound to Move / attack, Attack move, Stop, the six ability slots and Chat, from
/// <see cref="IInputBindingLabels"/> at start and on every <see cref="IInputBindingLabels.BindingsChanged"/>, so a
/// rebind or a keyboard layout change shows at once.
/// </summary>
public sealed class ControlsPagePresenter : IStartable, IDisposable
{
    private readonly ControlsPageView view;
    private readonly IInputBindingLabels labels;
    private bool started;

    /// <summary>
    /// Shows <paramref name="labels"/>' keys on <paramref name="view"/>.
    /// </summary>
    public ControlsPagePresenter(ControlsPageView view, IInputBindingLabels labels)
    {
        this.view = view;
        this.labels = labels;
    }

    /// <summary>
    /// Shows the keys and follows the bindings.
    /// </summary>
    public void Start()
    {
        if (started || view == null)
        {
            return;
        }

        started = true;
        labels.BindingsChanged += Refresh;
        Refresh();
    }

    /// <summary>
    /// Stops following the bindings.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        labels.BindingsChanged -= Refresh;
    }

    /// <summary>
    /// Shows every control's key as it is bound now.
    /// </summary>
    private void Refresh()
    {
        view.SetMovementKeys(
            labels.GetActionKeyLabel(PlayerInputService.MoveClickActionName),
            labels.GetActionKeyLabel(PlayerInputService.AttackMoveActionName),
            labels.GetActionKeyLabel(PlayerInputService.StopActionName));
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            view.SetAbilityKey(slot, labels.GetAbilityKeyLabel(slot));
        }

        view.SetChatKey(labels.GetActionKeyLabel(PlayerInputService.ChatActionName));
    }
}
