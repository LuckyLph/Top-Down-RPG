/// <summary>
/// Sends chat lines through the local player's <see cref="PlayerChat"/> (Docs/UiRevamp.md, Phase 12), which takes them
/// to the host. Nothing goes without a local player, or offline, where the player is not spawned.
/// </summary>
public sealed class LocalChatSender : IChatSender
{
    private readonly LocalPlayerTracker localPlayer;

    /// <summary>
    /// Sends through the player <paramref name="tracker"/> names as this machine's.
    /// </summary>
    public LocalChatSender(LocalPlayerTracker tracker)
    {
        localPlayer = tracker;
    }

    /// <summary>
    /// Has the local player say <paramref name="text"/>; returns whether it went.
    /// </summary>
    public bool Send(string text)
    {
        LocalPlayer player = localPlayer.Current;
        return player != null && player.Transform != null && player.Transform.TryGetComponent(out PlayerChat chat) && chat.Say(text);
    }
}
