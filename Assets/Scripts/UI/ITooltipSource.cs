using System;

/// <summary>
/// What a <see cref="TooltipTrigger"/> on the same object shows: a title, a line of numbers and a description, which
/// change when the source shows something else (Docs/DesignSystem.md, Components: Tooltip).
/// </summary>
public interface ITooltipSource
{
    /// <summary>
    /// Raised when what the tooltip would show changes, so a shown tooltip follows it.
    /// </summary>
    event Action TooltipChanged;

    /// <summary>
    /// The title, the numbers (an ability's cooldown, cast time and range; a weapon's damage and range) and the
    /// description to show now, the numbers or the description empty or null when there are none; false when there is
    /// nothing to show (an empty slot).
    /// </summary>
    bool TryGetTooltip(out string title, out string details, out string description);
}
