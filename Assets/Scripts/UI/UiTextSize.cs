/// <summary>
/// A text size of the UI theme (Docs/DesignSystem.md, Foundations: Type), smallest first. <see cref="UiTheme"/> holds the
/// font and the size in UI px of each. Append-only: prefabs store sizes by value.
/// </summary>
public enum UiTextSize
{
    Caption,
    Body,
    Heading,
    Title,
    Display,
}
