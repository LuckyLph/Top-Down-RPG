using System;

/// <summary>
/// Short labels for the keys the player has bound to actions, for HUD text that has to follow rebinding and the
/// keyboard layout.
/// </summary>
public interface IInputBindingLabels
{
    event Action BindingsChanged;

    /// <summary>
    /// A short label for the key or mouse button bound to the action named <paramref name="actionName"/>, such as
    /// "Q" or "RMB", or an empty string when there is no such action.
    /// </summary>
    string GetShortLabel(string actionName);
}
