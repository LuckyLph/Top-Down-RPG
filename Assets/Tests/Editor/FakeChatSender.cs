using System.Collections.Generic;

/// <summary>
/// An <see cref="IChatSender"/> that records every line it is given and says each went.
/// </summary>
public sealed class FakeChatSender : IChatSender
{
    public List<string> Sent { get; } = new();

    /// <summary>
    /// Records <paramref name="text"/>.
    /// </summary>
    public bool Send(string text)
    {
        Sent.Add(text);
        return true;
    }
}
