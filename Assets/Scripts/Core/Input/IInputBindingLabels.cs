using System;

/// <summary>
/// Short labels for the keys the player has bound to the ability slots and the other controls, for HUD and menu text that
/// has to follow rebinding and the keyboard layout.
/// </summary>
public interface IInputBindingLabels
{
    event Action BindingsChanged;

    /// <summary>
    /// A short label for the key or mouse button bound to ability <paramref name="slot"/>, such as "Q" or "RMB", or an
    /// empty string outside the bar.
    /// </summary>
    string GetAbilityKeyLabel(int slot);

    /// <summary>
    /// A short label for the key or mouse button bound to the <c>Player</c> map's action named
    /// <paramref name="actionName"/> (<see cref="PlayerInputService.MoveClickActionName"/> and the like), or an empty
    /// string for an action the map lacks.
    /// </summary>
    string GetActionKeyLabel(string actionName);
}
