/// <summary>
/// Sends the local player's chat lines (Docs/UiRevamp.md, Phase 12), so the chat can be tested without the network.
/// </summary>
public interface IChatSender
{
    /// <summary>
    /// Sends <paramref name="text"/> as the local player's line; returns whether it went.
    /// </summary>
    bool Send(string text);
}
