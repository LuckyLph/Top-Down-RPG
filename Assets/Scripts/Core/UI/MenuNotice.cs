/// <summary>
/// Why the main menu was reached (Docs/UiRevamp.md, Core: MenuNotice), on this machine only: the session's lifecycle
/// leaves it as a lost connection takes the game back to the menu (the host gone, or lost while joining, with the host's
/// address), and the main menu takes it once as it opens, to say so in a dialog. Main-scope singleton; never sent.
/// </summary>
public sealed class MenuNotice
{
    private MenuNoticeKind kind;
    private string address = string.Empty;

    public MenuNoticeKind Kind => kind;

    /// <summary>
    /// Leaves <paramref name="noticeKind"/> for the menu, about the host at <paramref name="hostAddress"/>, in place of
    /// any notice not taken yet.
    /// </summary>
    public void Leave(MenuNoticeKind noticeKind, string hostAddress)
    {
        kind = noticeKind;
        address = hostAddress ?? string.Empty;
    }

    /// <summary>
    /// The notice left, and its host's address, clearing it; false when there is none.
    /// </summary>
    public bool TryTake(out MenuNoticeKind noticeKind, out string hostAddress)
    {
        noticeKind = kind;
        hostAddress = address;
        kind = MenuNoticeKind.None;
        address = string.Empty;
        return noticeKind != MenuNoticeKind.None;
    }
}
