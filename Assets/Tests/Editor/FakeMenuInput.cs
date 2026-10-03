public sealed class FakeMenuInput : IMenuInput
{
    public bool BackPressedThisFrame { get; set; }
    public bool MenuPressedThisFrame { get; set; }
}
