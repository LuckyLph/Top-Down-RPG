/// <summary>
/// How this machine shows the popups of hits, ticks and heals that involve another player but not the local one
/// (Docs/UiRevamp.md, Phase 3): shown as every other popup, smaller, or hidden. Stored in the settings file by value,
/// so values are only appended.
/// </summary>
public enum AllyPopups
{
    Shown = 0,
    Smaller = 1,
    Hidden = 2,
}
