/// <summary>
/// An <see cref="IChatInput"/> tests press by hand: the Chat key reads as pressed until the test lets it go.
/// </summary>
public sealed class FakeChatInput : IChatInput
{
    public bool ChatPressedThisFrame { get; set; }
}
