using System;

/// <summary>
/// Short labels for the keys the player has bound to the ability slots, for HUD text that has to follow rebinding and the
/// keyboard layout.
/// </summary>
public interface IInputBindingLabels
{
    event Action BindingsChanged;

    /// <summary>
    /// A short label for the key or mouse button bound to ability <paramref name="slot"/>, such as "Q" or "RMB", or an
    /// empty string outside the bar.
    /// </summary>
    string GetAbilityKeyLabel(int slot);
}
