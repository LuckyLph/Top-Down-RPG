/// <summary>
/// <see cref="IMenuInput"/> whose back and menu presses tests set directly instead of reading devices.
/// </summary>
public sealed class FakeMenuInput : IMenuInput
{
    public bool BackPressedThisFrame { get; set; }
    public bool MenuPressedThisFrame { get; set; }
}
