/// <summary>
/// A colour role of the UI theme (Docs/DesignSystem.md, Foundations: Colour roles). UI code and prefabs name roles,
/// never raw colours; <see cref="UiTheme"/> holds the colour of each. Append-only: prefabs store roles by value.
/// </summary>
public enum UiColour
{
    Text,
    TextOnControl,
    TextMuted,
    TextDisabled,
    Placeholder,
    Accent,
    Surface,
    SurfaceEmpty,
    SurfaceRaised,
    Scrim,
    ScrimModal,
    Shade,
    ControlNormal,
    ControlHighlighted,
    ControlPressed,
    ControlDisabled,
    Danger,
    Positive,
    Health,
    HealthTrack,
    Icon,
    IconMissing,
    Border,
    Outline,
}
