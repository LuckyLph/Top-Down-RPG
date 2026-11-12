/// <summary>
/// An <see cref="IClock"/> on unscaled time (<see cref="UnityEngine.Time.unscaledTime"/>), which runs on while
/// <see cref="TimeFreeze"/> holds game time still: the Character screen's preview stage animates on it, so the character
/// moves while the game is paused (Docs/UiRevamp.md, Phase 7). Gameplay never reads it.
/// </summary>
public sealed class UnscaledClock : IClock
{
    public float Time => UnityEngine.Time.unscaledTime;
}
