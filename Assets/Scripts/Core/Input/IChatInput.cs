/// <summary>
/// The chat key for the local machine (Docs/UiRevamp.md, Phase 12), read from the input asset so the chat can be tested
/// with a fake.
/// </summary>
public interface IChatInput
{
    /// <summary>
    /// Whether the <c>Player</c> map's Chat key (Enter by default) went down this frame; false while gameplay input is
    /// off, so never while the chat's field or a menu is open.
    /// </summary>
    bool ChatPressedThisFrame { get; }
}
