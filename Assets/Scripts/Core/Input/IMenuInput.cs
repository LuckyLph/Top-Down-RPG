/// <summary>
/// Menu keys for the local machine, read from the input asset so menus can be tested with a fake.
/// </summary>
public interface IMenuInput
{
    /// <summary>
    /// Whether the <c>UI</c> map's Cancel (Escape, gamepad East) went down this frame.
    /// </summary>
    bool BackPressedThisFrame { get; }
}
