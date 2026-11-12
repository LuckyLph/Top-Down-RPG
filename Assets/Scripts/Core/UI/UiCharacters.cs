using System.Text;

/// <summary>
/// The characters the UI's fonts hold (Docs/DesignSystem.md, Foundations: Type): printable ASCII, Latin-1 (key names on
/// other keyboard layouts), dashes, curly quotes, the bullet, the ellipsis and the zero-width space TMP's input field
/// shows when empty. The font writer puts exactly these in every Galmuri atlas, and what a player types into the chat is
/// kept to them (Docs/UiRevamp.md, Phase 12).
/// </summary>
public static class UiCharacters
{
    public const char ZeroWidthSpace = '​';
    private const string Extras = "–—‘’“”•…​";

    /// <summary>
    /// Every character the atlases hold, in the order they are added.
    /// </summary>
    public static readonly string All = Build();

    /// <summary>
    /// Whether the atlases hold <paramref name="character"/>. Allocates nothing.
    /// </summary>
    public static bool Contains(char character)
    {
        return (character >= ' ' && character <= '~') || (character >= ' ' && character <= 'ÿ') || Extras.IndexOf(character) >= 0;
    }

    /// <summary>
    /// Builds <see cref="All"/>.
    /// </summary>
    private static string Build()
    {
        StringBuilder characters = new();
        for (char c = ' '; c <= '~'; c++)
        {
            characters.Append(c);
        }

        for (char c = ' '; c <= 'ÿ'; c++)
        {
            characters.Append(c);
        }

        characters.Append(Extras);
        return characters.ToString();
    }
}
