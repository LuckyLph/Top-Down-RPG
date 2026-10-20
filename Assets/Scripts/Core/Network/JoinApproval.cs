using System;
using System.Text;

/// <summary>
/// The host's rule for letting a client in: the client must run the host's build version and the party must have
/// room. The payload a client sends is its build version, then its player id (Docs/WorldStreaming.md: Player
/// identity), on a line of its own. Refusals reach the client as Netcode disconnect reasons, tagged so the client can
/// tell them apart from transport messages.
/// </summary>
public static class JoinApproval
{
    private const string RefusalTag = "TopDownRPG.Refused:";
    private const char Separator = '\n';

    /// <summary>
    /// The connection payload a joining client sends: its build version, then its player id. Line breaks in either are
    /// dropped, since a line break separates them.
    /// </summary>
    public static byte[] CreatePayload(string version, string playerId)
    {
        return Encoding.UTF8.GetBytes(Clean(version) + Separator + Clean(playerId));
    }

    /// <summary>
    /// Reads the build version and the player id from a client's <paramref name="payload"/>; a missing part reads as
    /// an empty string, so a payload from a build that sent only its version has no player id.
    /// </summary>
    public static void ReadPayload(byte[] payload, out string version, out string playerId)
    {
        string text = payload == null || payload.Length == 0 ? string.Empty : Encoding.UTF8.GetString(payload);
        int separator = text.IndexOf(Separator);
        version = separator < 0 ? text : text.Substring(0, separator);
        playerId = separator < 0 ? string.Empty : text.Substring(separator + 1);
    }

    /// <summary>
    /// Returns null when a client with <paramref name="payload"/> may join a session that already holds
    /// <paramref name="playerCount"/> players (the host included), otherwise the reason it is refused.
    /// </summary>
    public static string Evaluate(byte[] payload, string hostVersion, int playerCount, int maxPlayers)
    {
        ReadPayload(payload, out string clientVersion, out _);
        if (clientVersion != hostVersion)
        {
            string shown = clientVersion.Length > 0 ? clientVersion : "an unknown version";
            return $"the host runs version {hostVersion} and this game runs {shown}.";
        }

        if (playerCount >= maxPlayers)
        {
            return $"the game is full ({maxPlayers} players).";
        }

        return null;
    }

    /// <summary>
    /// Wraps a refusal from <see cref="Evaluate"/> as a disconnect reason.
    /// </summary>
    public static string EncodeRefusal(string reason)
    {
        return RefusalTag + reason;
    }

    /// <summary>
    /// Reads a refusal back from a client's disconnect reason; false for any other reason, such as a timeout.
    /// </summary>
    public static bool TryDecodeRefusal(string disconnectReason, out string reason)
    {
        if (string.IsNullOrEmpty(disconnectReason) || !disconnectReason.StartsWith(RefusalTag, StringComparison.Ordinal))
        {
            reason = null;
            return false;
        }

        reason = disconnectReason.Substring(RefusalTag.Length);
        return true;
    }

    /// <summary>
    /// <paramref name="text"/> without line breaks; empty for null.
    /// </summary>
    private static string Clean(string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }
}
