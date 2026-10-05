using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using VContainer.Unity;

/// <summary>
/// Drives the <see cref="OptionsMenuView"/>: builds a row per rebindable action of <see cref="InputBindings"/> under
/// its section, rebinds a row when its binding is selected (listening starts on the next tick, so the click itself is
/// never captured), names the action that moved on a swap, resets one row or all of them after a confirmation, and
/// closes on Back. While open it is a layer on the <see cref="MenuStack"/>; Back closes the confirmation, or else the
/// screen, and does nothing while a rebind is pending or listening (the rebind itself takes Escape as cancel).
/// </summary>
public sealed class OptionsMenuController : IStartable, ITickable, IDisposable, IMenuLayer
{
    public const string ListeningPromptFormat = "Press a key or mouse button for {0}.\nEsc to cancel.";
    public const string MovedNoticeFormat = "{0} moved to {1}.";
    public const string ResetAllNotice = "All controls are back to their defaults.";

    private readonly OptionsMenuView view;
    private readonly InputBindings bindings;
    private readonly MenuStack menus;
    private readonly UISelection selection;
    private readonly List<KeyBindingRowView> rows = new();
    private Action onClosed;
    private int pendingRebind = -1;
    private int listeningRow = -1;

    /// <summary>
    /// Creates a closed options controller that edits <paramref name="bindings"/> through <paramref name="view"/>.
    /// </summary>
    public OptionsMenuController(OptionsMenuView view, InputBindings bindings, MenuStack menus, UISelection selection)
    {
        this.view = view;
        this.bindings = bindings;
        this.menus = menus;
        this.selection = selection;
    }

    public bool IsOpen => view.IsShown;
    public bool IsListening => listeningRow >= 0;
    internal int RowCount => rows.Count;

    /// <summary>
    /// The row built for the rebindable action at <paramref name="index"/>, in <see cref="InputBindings"/> order.
    /// </summary>
    internal KeyBindingRowView GetRow(int index)
    {
        return rows[index];
    }

    /// <summary>
    /// Hides the screen, builds a section header for each new section and a labelled row per rebindable action,
    /// wires the buttons, and keeps the rows' binding labels in sync with <see cref="InputBindings.BindingsChanged"/>.
    /// </summary>
    public void Start()
    {
        view.SetShown(false);
        view.HideListening();
        view.SetConfirmShown(false);

        string section = null;
        for (int i = 0; i < bindings.Count; i++)
        {
            InputAction action = bindings.GetAction(i);
            string actionSection = bindings.GetSection(action);
            if (actionSection != section)
            {
                view.AddSectionHeader(actionSection);
                section = actionSection;
            }

            KeyBindingRowView row = view.AddRow();
            row.SetLabel(bindings.GetDisplayName(action));
            int index = i;
            row.BindingButton.onClick.AddListener(() => RequestRebind(index));
            row.ResetButton.onClick.AddListener(() => ResetRow(index));
            rows.Add(row);
        }

        view.ResetAllButton.onClick.AddListener(AskResetAll);
        view.BackButton.onClick.AddListener(Close);
        view.ConfirmYesButton.onClick.AddListener(ConfirmResetAll);
        view.ConfirmNoButton.onClick.AddListener(CancelResetAll);
        bindings.BindingsChanged += RefreshRows;
        RefreshRows();
    }

    /// <summary>
    /// Stops following binding changes, leaves the menu stack and cancels a rebind that is still listening.
    /// </summary>
    public void Dispose()
    {
        bindings.BindingsChanged -= RefreshRows;
        menus.Remove(this);
        if (IsListening)
        {
            bindings.CancelRebind();
        }
    }

    /// <summary>
    /// Shows the screen with the first binding selected. <paramref name="closed"/> runs once it closes.
    /// </summary>
    public void Open(Action closed)
    {
        onClosed = closed;
        pendingRebind = -1;
        view.SetNotice(string.Empty);
        view.SetConfirmShown(false);
        view.HideListening();
        RefreshRows();
        view.SetShown(true);
        menus.Push(this);
        selection.Select(rows.Count > 0 ? rows[0].BindingButton : view.BackButton);
    }

    /// <summary>
    /// Hides the screen, cancelling a rebind that is listening, and runs the callback given to <see cref="Open"/>.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        pendingRebind = -1;
        if (IsListening)
        {
            bindings.CancelRebind();
        }

        view.SetConfirmShown(false);
        view.HideListening();
        view.SetShown(false);
        menus.Remove(this);
        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    /// <summary>
    /// Starts listening for the rebind requested on an earlier frame, so the click that asked for it is not
    /// captured as the new binding.
    /// </summary>
    public void Tick()
    {
        if (IsOpen && pendingRebind >= 0)
        {
            int row = pendingRebind;
            pendingRebind = -1;
            BeginListening(row);
        }
    }

    /// <summary>
    /// Closes the Reset all confirmation, or else the screen. Ignored while a rebind is pending or listening.
    /// </summary>
    public void HandleBack()
    {
        if (IsListening || pendingRebind >= 0)
        {
            return;
        }

        if (view.IsConfirmShown)
        {
            CancelResetAll();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Binding button of row <paramref name="index"/>: queues its rebind for the next tick. Ignored while closed,
    /// listening or confirming.
    /// </summary>
    private void RequestRebind(int index)
    {
        if (IsOpen && !IsListening && !view.IsConfirmShown)
        {
            pendingRebind = index;
        }
    }

    /// <summary>
    /// Shows the listening prompt for row <paramref name="index"/>, clears the selection and starts the
    /// interactive rebind; if it cannot start, hides the prompt and reselects the row's binding button.
    /// </summary>
    private void BeginListening(int index)
    {
        InputAction action = bindings.GetAction(index);
        listeningRow = index;
        view.SetNotice(string.Empty);
        view.ShowListening(string.Format(ListeningPromptFormat, bindings.GetDisplayName(action)));
        selection.Select(null);
        if (!bindings.StartRebind(action, HandleRebindFinished))
        {
            listeningRow = -1;
            view.HideListening();
            selection.Select(rows[index].BindingButton);
        }
    }

    /// <summary>
    /// Ends listening: hides the prompt, names the action that moved when the new input was assigned, and
    /// reselects the row's binding button if the screen is still open.
    /// </summary>
    private void HandleRebindFinished(RebindOutcome outcome, InputAction swappedWith)
    {
        int row = listeningRow;
        listeningRow = -1;
        view.HideListening();
        if (outcome == RebindOutcome.Assigned)
        {
            ShowMoved(swappedWith);
        }

        if (IsOpen && row >= 0)
        {
            selection.Select(rows[row].BindingButton);
        }
    }

    /// <summary>
    /// Reset button of row <paramref name="index"/>: puts that action back on its default input and names the
    /// action that moved, if any. Ignored while listening or confirming.
    /// </summary>
    private void ResetRow(int index)
    {
        if (IsListening || view.IsConfirmShown)
        {
            return;
        }

        bindings.Reset(bindings.GetAction(index), out InputAction swappedWith);
        ShowMoved(swappedWith);
    }

    /// <summary>
    /// Reset all button: shows the confirmation with Cancel selected. Ignored while listening.
    /// </summary>
    private void AskResetAll()
    {
        if (IsListening)
        {
            return;
        }

        view.SetConfirmShown(true);
        selection.Select(view.ConfirmNoButton);
    }

    /// <summary>
    /// Confirmation accepted: puts every action back on its default, hides the confirmation and says so.
    /// </summary>
    private void ConfirmResetAll()
    {
        bindings.ResetAll();
        view.SetConfirmShown(false);
        view.SetNotice(ResetAllNotice);
        selection.Select(view.ResetAllButton);
    }

    /// <summary>
    /// Confirmation declined: hides it and reselects Reset all.
    /// </summary>
    private void CancelResetAll()
    {
        view.SetConfirmShown(false);
        selection.Select(view.ResetAllButton);
    }

    /// <summary>
    /// Shows which action <paramref name="swappedWith"/> moved to which input, or clears the notice when nothing
    /// was swapped.
    /// </summary>
    private void ShowMoved(InputAction swappedWith)
    {
        view.SetNotice(swappedWith != null
            ? string.Format(MovedNoticeFormat, bindings.GetDisplayName(swappedWith), bindings.GetLabel(swappedWith))
            : string.Empty);
    }

    /// <summary>
    /// Updates every row's binding button with its action's current binding label.
    /// </summary>
    private void RefreshRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].SetBinding(bindings.GetLabel(bindings.GetAction(i)));
        }
    }
}
