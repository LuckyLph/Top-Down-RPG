/// <summary>
/// An open menu on the <see cref="MenuStack"/>. While it is on top it gets Back (Escape, gamepad East) and the Menu
/// key (gamepad Start), at most once per frame.
/// </summary>
public interface IMenuLayer
{
    void HandleBack();
}
