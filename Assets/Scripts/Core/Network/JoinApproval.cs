using System;
using System.Text;

/// <summary>
/// The host's rule for letting a client in: the client must run the host's build version and the party must have
/// room. Refusals reach the client as Netcode disconnect reasons, tagged so the client can tell them apart from
/// transport messages.
/// </summary>
public static class JoinApproval
{
    private const string RefusalTag = "TopDownRPG.Refused:";

    /// <summary>
    /// The connection payload a joining client sends: its build version.
    /// </summary>
    public static byte[] CreatePayload(string version)
    {
        return Encoding.UTF8.GetBytes(version ?? string.Empty);
    }

    /// <summary>
    /// Returns null when a client with <paramref name="payload"/> may join a session that already holds
    /// <paramref name="playerCount"/> players (the host included), otherwise the reason it is refused.
    /// </summary>
    public static string Evaluate(byte[] payload, string hostVersion, int playerCount, int maxPlayers)
    {
        string clientVersion = payload == null || payload.Length == 0 ? string.Empty : Encoding.UTF8.GetString(payload);
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
}
