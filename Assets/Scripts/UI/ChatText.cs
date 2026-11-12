using System.Text;

/// <summary>
/// What a chat line may hold (Docs/UiRevamp.md, Phase 12): the characters the UI's fonts hold
/// (<see cref="UiCharacters"/>) but the zero-width space, at most <see cref="MaxLength"/> of them. The field keeps typed
/// text to them as it is typed (<see cref="Validate"/>), the sender and the host clean every line before it goes on
/// (<see cref="Clean"/>), and a line is escaped before it is shown as rich text, so nothing a player types reads as a
/// tag (<see cref="Escape"/>).
/// </summary>
public static class ChatText
{
    public const int MaxLength = 80;

    /// <summary>
    /// Whether <paramref name="character"/> may be typed: one the fonts hold, but the zero-width space, which
    /// <see cref="Escape"/> uses and the input field shows when empty.
    /// </summary>
    public static bool IsAllowed(char character)
    {
        return character != UiCharacters.ZeroWidthSpace && UiCharacters.Contains(character);
    }

    /// <summary>
    /// The input field's check of each typed or pasted character: <paramref name="added"/> when it is allowed, else
    /// the null character, which the field drops.
    /// </summary>
    public static char Validate(string text, int index, char added)
    {
        return IsAllowed(added) ? added : '\0';
    }

    /// <summary>
    /// <paramref name="text"/> with every character that is not allowed dropped, the white space at its ends trimmed,
    /// and cut to <see cref="MaxLength"/> characters; empty for null. Returns the text itself when it is clean already.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int start = 0;
        int end = text.Length;
        while (start < end && (!IsAllowed(text[start]) || char.IsWhiteSpace(text[start])))
        {
            start++;
        }

        while (end > start && (!IsAllowed(text[end - 1]) || char.IsWhiteSpace(text[end - 1])))
        {
            end--;
        }

        bool clean = true;
        for (int i = start; i < end && clean; i++)
        {
            clean = IsAllowed(text[i]);
        }

        if (clean && end - start <= MaxLength)
        {
            return start == 0 && end == text.Length ? text : text.Substring(start, end - start);
        }

        StringBuilder kept = new(MaxLength);
        for (int i = start; i < end && kept.Length < MaxLength; i++)
        {
            if (IsAllowed(text[i]))
            {
                kept.Append(text[i]);
            }
        }

        return kept.ToString().TrimEnd();
    }

    /// <summary>
    /// <paramref name="text"/> made safe to show as rich text: each <c>&lt;</c> followed by a zero-width space, so no
    /// tag can start there and the text shows as typed.
    /// </summary>
    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
        {
            return text ?? string.Empty;
        }

        return text.Replace("<", "<" + UiCharacters.ZeroWidthSpace);
    }
}
