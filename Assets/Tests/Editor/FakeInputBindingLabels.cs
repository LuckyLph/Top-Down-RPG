using System;
using System.Collections.Generic;

/// <summary>
/// In-memory <see cref="IInputBindingLabels"/> whose ability and action key labels tests set directly.
/// </summary>
public sealed class FakeInputBindingLabels : IInputBindingLabels
{
    private readonly Dictionary<int, string> labels = new();
    private readonly Dictionary<string, string> actionLabels = new();

    public event Action BindingsChanged;

    /// <summary>
    /// Returns the label set for <paramref name="slot"/>, or an empty string when none was set.
    /// </summary>
    public string GetAbilityKeyLabel(int slot)
    {
        return labels.TryGetValue(slot, out string label) ? label : string.Empty;
    }

    /// <summary>
    /// Returns the label set for the action named <paramref name="actionName"/>, or an empty string when none was set.
    /// </summary>
    public string GetActionKeyLabel(string actionName)
    {
        return actionLabels.TryGetValue(actionName, out string label) ? label : string.Empty;
    }

    /// <summary>
    /// Sets the label for the action named <paramref name="actionName"/> and raises <see cref="BindingsChanged"/>.
    /// </summary>
    public void SetAction(string actionName, string label)
    {
        actionLabels[actionName] = label;
        BindingsChanged?.Invoke();
    }

    /// <summary>
    /// Sets the label for <paramref name="slot"/> and raises <see cref="BindingsChanged"/>, as a rebind would.
    /// </summary>
    public void Set(int slot, string label)
    {
        labels[slot] = label;
        BindingsChanged?.Invoke();
    }
}
