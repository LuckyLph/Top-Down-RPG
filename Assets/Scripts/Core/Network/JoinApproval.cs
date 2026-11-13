using System;
using System.Text;

/// <summary>
/// The host's rule for letting a client in: the client must run the host's build version and the party must have
/// room. The payload a client sends is its build version, then its player id (Docs/WorldStreaming.md: Player
/// identity), on a line of its own. A refusal names its kind and what the client needs to say why
/// (Docs/UiRevamp.md, Phase 9): the host's version for another build, the most players for a full party. Refusals
/// reach the client as Netcode disconnect reasons, tagged so the client can tell them apart from transport messages.
/// </summary>
public static class JoinApproval
{
    private const string RefusalTag = "TopDownRPG.Refused:";
    private const string OtherBuildKind = "build:";
    private const string FullKind = "full:";
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
    /// <paramref name="playerCount"/> players (the host included), otherwise the refusal: another build's
    /// (<see cref="OtherBuild"/>) before a full party's (<see cref="Full"/>).
    /// </summary>
    public static string Evaluate(byte[] payload, string hostVersion, int playerCount, int maxPlayers)
    {
        ReadPayload(payload, out string clientVersion, out _);
        if (clientVersion != hostVersion)
        {
            return OtherBuild(hostVersion);
        }

        if (playerCount >= maxPlayers)
        {
            return Full(maxPlayers);
        }

        return null;
    }

    /// <summary>
    /// The refusal of a client running another build than the host's <paramref name="hostVersion"/>.
    /// </summary>
    public static string OtherBuild(string hostVersion)
    {
        return OtherBuildKind + Clean(hostVersion);
    }

    /// <summary>
    /// The refusal of a client joining a party that already holds <paramref name="maxPlayers"/>.
    /// </summary>
    public static string Full(int maxPlayers)
    {
        return FullKind + maxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether <paramref name="refusal"/> refused another build, with the host's version.
    /// </summary>
    public static bool IsOtherBuild(string refusal, out string hostVersion)
    {
        hostVersion = refusal != null && refusal.StartsWith(OtherBuildKind, StringComparison.Ordinal) ? refusal.Substring(OtherBuildKind.Length) : null;
        return hostVersion != null;
    }

    /// <summary>
    /// Whether <paramref name="refusal"/> refused a full party, with how many players it holds at most.
    /// </summary>
    public static bool IsFull(string refusal, out int maxPlayers)
    {
        maxPlayers = 0;
        return refusal != null && refusal.StartsWith(FullKind, StringComparison.Ordinal)
            && int.TryParse(refusal.Substring(FullKind.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out maxPlayers);
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
