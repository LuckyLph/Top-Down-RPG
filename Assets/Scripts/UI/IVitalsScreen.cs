/// <summary>
/// A full screen opened in game that keeps the game's state in view online (Docs/UiRevamp.md, Full screens in game): the
/// options screen and the Character screen. <see cref="VitalsStripPresenter"/> shows its vitals strip while it is open
/// online and puts its warning on its foot line.
/// </summary>
public interface IVitalsScreen
{
    bool IsOpen { get; }

    VitalsStripView Vitals { get; }

    /// <summary>
    /// Shows <paramref name="text"/> as the screen's warning, in Accent on its foot line, or clears it for null.
    /// </summary>
    void SetWarning(string text);
}
