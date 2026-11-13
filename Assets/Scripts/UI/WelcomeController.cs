using System;
using UnityEngine;
using UnityEngine.UI;
using VContainer.Unity;

/// <summary>
/// Menu-scope entry point for the first run's Welcome panel (Docs/UiRevamp.md, Phase 11), on this machine only: shown
/// before the main menu while the first run is not over (<see cref="UserSettings.FirstRunDone"/>), it offers the UI size
/// (stepping through the sizes the display fits, as the options screen does), Reduce flashing and Reduce motion, each
/// applied and saved at once, and describes the focused row as the options screen does. Continue, or Back (Escape),
/// accepts what is set, marks the first run over and closes it. Up and Down move along the rows and Continue, looping.
/// </summary>
public sealed class WelcomeController : IStartable, ITickable, IDisposable, IMenuLayer
{
    public const string ContinueLabel = "Continue";

    private readonly WelcomeView view;
    private readonly UserSettings settings;
    private readonly UiScale uiScale;
    private readonly MenuStack menus;
    private readonly UISelection selection;
    private readonly Selectable[] looped = new Selectable[4];
    private Action onClosed;
    private GameObject described;
    private bool started;

    /// <summary>
    /// Runs <paramref name="view"/> over <paramref name="settings"/> and <paramref name="uiScale"/>'s sizes, as a layer
    /// of <paramref name="menus"/>, moving focus through <paramref name="selection"/>.
    /// </summary>
    public WelcomeController(WelcomeView view, UserSettings settings, UiScale uiScale, MenuStack menus, UISelection selection)
    {
        this.view = view;
        this.settings = settings;
        this.uiScale = uiScale;
        this.menus = menus;
        this.selection = selection;
    }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Whether the first run is not over, so the main menu shows this panel first.
    /// </summary>
    public bool ShouldShow => !settings.FirstRunDone;

    private int UiSizeStep => Mathf.Clamp(settings.UiSizeStep, 0, Mathf.Min(uiScale.StepsThatFit, OptionsMenuController.UiSizeNames.Length - 1));

    /// <summary>
    /// Labels and wires the rows and Continue, follows the settings, and hides the panel.
    /// </summary>
    public void Start()
    {
        if (started || view == null)
        {
            return;
        }

        started = true;
        view.UiSizeRow.SetLabel(OptionsMenuController.UiSizeLabel);
        view.ReduceFlashingRow.SetLabel(OptionsMenuController.ReduceFlashingLabel);
        view.ReduceMotionRow.SetLabel(OptionsMenuController.ReduceMotionLabel);
        view.UiSizeRow.BindingButton.onClick.AddListener(NextUiSize);
        view.ReduceFlashingRow.BindingButton.onClick.AddListener(ToggleReduceFlashing);
        view.ReduceMotionRow.BindingButton.onClick.AddListener(ToggleReduceMotion);
        view.ContinueButton.onClick.AddListener(Finish);
        settings.UiSizeStepChanged += Refresh;
        settings.ReduceFlashingChanged += Refresh;
        settings.ReduceMotionChanged += Refresh;
        uiScale.Changed += Refresh;
        looped[0] = view.UiSizeRow.BindingButton;
        looped[1] = view.ReduceFlashingRow.BindingButton;
        looped[2] = view.ReduceMotionRow.BindingButton;
        looped[3] = view.ContinueButton;
        GameMenuView.Loop(looped, looped.Length, true);
        view.SetShown(false);
    }

    /// <summary>
    /// Stops following the settings and unwires the panel.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        settings.UiSizeStepChanged -= Refresh;
        settings.ReduceFlashingChanged -= Refresh;
        settings.ReduceMotionChanged -= Refresh;
        uiScale.Changed -= Refresh;
        view.UiSizeRow.BindingButton.onClick.RemoveListener(NextUiSize);
        view.ReduceFlashingRow.BindingButton.onClick.RemoveListener(ToggleReduceFlashing);
        view.ReduceMotionRow.BindingButton.onClick.RemoveListener(ToggleReduceMotion);
        view.ContinueButton.onClick.RemoveListener(Finish);
    }

    /// <summary>
    /// Shows the panel with the UI size selected and described. <paramref name="closed"/> runs once it closes.
    /// </summary>
    public void Open(Action closed)
    {
        if (!started || IsOpen)
        {
            return;
        }

        IsOpen = true;
        onClosed = closed;
        Refresh();
        view.SetShown(true);
        menus.Push(this);
        selection.Select(view.UiSizeRow.BindingButton);
        Describe(view.UiSizeRow.BindingButton.gameObject);
    }

    /// <summary>
    /// Back (Escape): accepts what is set, as Continue does.
    /// </summary>
    public void HandleBack()
    {
        Finish();
    }

    /// <summary>
    /// While open, describes the focused row when the selection moves.
    /// </summary>
    public void Tick()
    {
        if (IsOpen && selection.Current != described)
        {
            Describe(selection.Current);
        }
    }

    /// <summary>
    /// Continue: marks the first run over, hides the panel and runs the callback given to <see cref="Open"/>.
    /// </summary>
    private void Finish()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        settings.SetFirstRunDone();
        view.SetShown(false);
        menus.Remove(this);
        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    /// <summary>
    /// UI size: keeps the next size the display fits, after the largest Normal.
    /// </summary>
    private void NextUiSize()
    {
        int sizes = Mathf.Min(uiScale.StepsThatFit, OptionsMenuController.UiSizeNames.Length - 1) + 1;
        settings.SetUiSizeStep((UiSizeStep + 1) % sizes);
    }

    /// <summary>
    /// Reduce flashing: turns it on or off.
    /// </summary>
    private void ToggleReduceFlashing()
    {
        settings.SetReduceFlashing(!settings.ReduceFlashing);
    }

    /// <summary>
    /// Reduce motion: turns it on or off.
    /// </summary>
    private void ToggleReduceMotion()
    {
        settings.SetReduceMotion(!settings.ReduceMotion);
    }

    /// <summary>
    /// Shows each row's value: the UI size the display shows and each toggle's state, and describes the focus again.
    /// </summary>
    private void Refresh()
    {
        view.UiSizeRow.SetBinding(OptionsMenuController.UiSizeNames[UiSizeStep]);
        Show(view.ReduceFlashingRow, settings.ReduceFlashing);
        Show(view.ReduceMotionRow, settings.ReduceMotion);
        view.UiSizeRow.SetResetShown(false);
        view.ReduceFlashingRow.SetResetShown(false);
        view.ReduceMotionRow.SetResetShown(false);
        if (IsOpen)
        {
            Describe(selection.Current);
        }
    }

    /// <summary>
    /// Shows a toggle row's state on its button and in its box.
    /// </summary>
    private static void Show(KeyBindingRowView row, bool on)
    {
        row.SetBinding(on ? OptionsMenuController.OnName : OptionsMenuController.OffName);
        row.SetToggle(on);
    }

    /// <summary>
    /// Shows the description of the row <paramref name="current"/> is, the UI size's saying when no larger size fits;
    /// none on Continue.
    /// </summary>
    private void Describe(GameObject current)
    {
        described = current;
        string text = string.Empty;
        if (current == view.UiSizeRow.BindingButton.gameObject)
        {
            text = uiScale.StepsThatFit == 0 ? OptionsMenuController.UiSizeDescription + "\n" + OptionsMenuController.UiSizeNoLargerSize : OptionsMenuController.UiSizeDescription;
        }
        else if (current == view.ReduceFlashingRow.BindingButton.gameObject)
        {
            text = OptionsMenuController.ReduceFlashingDescription;
        }
        else if (current == view.ReduceMotionRow.BindingButton.gameObject)
        {
            text = OptionsMenuController.ReduceMotionDescription;
        }

        view.SetDescription(text);
    }
}
