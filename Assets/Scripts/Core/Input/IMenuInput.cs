/// <summary>
/// Menu keys for the local machine, read from the input asset so menus can be tested with a fake.
/// </summary>
public interface IMenuInput
{
    /// <summary>
    /// Whether the <c>UI</c> map's Cancel (Escape, gamepad East) went down this frame.
    /// </summary>
    bool BackPressedThisFrame { get; }

    /// <summary>
    /// Whether the <c>Menus</c> map's Menu key (Escape, gamepad Start) went down this frame. The map is only enabled
    /// in game, outside transitions.
    /// </summary>
    bool MenuPressedThisFrame { get; }
}
