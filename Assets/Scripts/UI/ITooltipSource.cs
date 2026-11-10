using System;

/// <summary>
/// What a <see cref="TooltipTrigger"/> on the same object shows: a title and a description, which change when the
/// source shows something else (Docs/DesignSystem.md, Components: Tooltip).
/// </summary>
public interface ITooltipSource
{
    /// <summary>
    /// Raised when what the tooltip would show changes, so a shown tooltip follows it.
    /// </summary>
    event Action TooltipChanged;

    /// <summary>
    /// The title and description to show now; false when there is nothing to show (an empty slot).
    /// </summary>
    bool TryGetTooltip(out string title, out string description);
}
