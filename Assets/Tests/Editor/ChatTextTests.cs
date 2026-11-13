using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for what a chat line may hold (Docs/UiRevamp.md, Phase 12): the characters the UI's fonts hold
/// (<see cref="UiCharacters"/>, the same set the font writer puts in the atlases) but the zero-width space, at most 80;
/// the field's check of each typed character, and the HUD's field kept to them; a line cleaned before it goes on, and
/// escaped before it shows as rich text.
/// </summary>
public class ChatTextTests
{
    /// <summary>
    /// The fonts' characters are the font writer's: printable ASCII, Latin-1, the dashes, curly quotes, bullet, ellipsis
    /// and the zero-width space, and <see cref="UiCharacters.Contains"/> agrees with the list for every character up to
    /// U+2100.
    /// </summary>
    [Test]
    public void TheFontsCharacters_AreTheFontWritersOnes()
    {
        Assert.That(UiFonts.Characters, Is.EqualTo(UiCharacters.All));
        Assert.That(UiCharacters.All.Length, Is.EqualTo(95 + 96 + 9));
        for (int c = 0; c <= 0x2100; c++)
        {
            Assert.That(UiCharacters.Contains((char)c), Is.EqualTo(UiCharacters.All.IndexOf((char)c) >= 0), $"U+{c:X4}");
        }
    }

    /// <summary>
    /// Letters, digits, punctuation, accented letters and curly quotes may be typed; the zero-width space, line breaks,
    /// tabs, other scripts and the halves of an emoji may not, and the field's check drops them.
    /// </summary>
    [Test]
    public void TheFontsCharacters_MayBeTyped_ButNotTheZeroWidthSpaceOrOthers()
    {
        foreach (char allowed in "aZ09 !?.:<>~éÉçà–’…")
        {
            Assert.That(ChatText.IsAllowed(allowed), Is.True, $"'{allowed}'");
            Assert.That(ChatText.Validate("so far", 6, allowed), Is.EqualTo(allowed));
        }

        foreach (char refused in new[] { UiCharacters.ZeroWidthSpace, '\n', '\r', '\t', '一', 'Ж', '\uD83D', '\uDE00', '€' })
        {
            Assert.That(ChatText.IsAllowed(refused), Is.False, $"U+{(int)refused:X4}");
            Assert.That(ChatText.Validate("so far", 6, refused), Is.EqualTo('\0'));
        }
    }

    /// <summary>
    /// A clean line comes back as it is; otherwise the characters not allowed are dropped, the white space at its ends
    /// trimmed, and it is cut to 80 characters; null and blank lines are empty.
    /// </summary>
    [Test]
    public void ALine_IsCleaned_ToItsCharactersTrimmedAndAtMost80()
    {
        const string clean = "on my way";
        Assert.That(ChatText.Clean(clean), Is.SameAs(clean));
        Assert.That(ChatText.Clean("  on my一 way\n "), Is.EqualTo("on my way"));
        Assert.That(ChatText.Clean("​hi​"), Is.EqualTo("hi"));
        Assert.That(ChatText.Clean(null), Is.Empty);
        Assert.That(ChatText.Clean("   \t "), Is.Empty);

        string longLine = new('a', 100);
        Assert.That(ChatText.Clean(longLine), Is.EqualTo(new string('a', ChatText.MaxLength)));
        Assert.That(ChatText.Clean(new string('一', 10) + longLine).Length, Is.EqualTo(ChatText.MaxLength), "Dropped characters do not count.");
    }

    /// <summary>
    /// Escaped, a line's every '&lt;' is followed by a zero-width space, so "&lt;color=red&gt;" shows as typed; a line
    /// without one comes back as it is.
    /// </summary>
    [Test]
    public void ALine_IsEscaped_SoNoTagStartsInIt()
    {
        Assert.That(ChatText.Escape("<color=red>hi</color>"), Is.EqualTo("<​color=red>hi<​/color>"));
        const string plain = "on my way";
        Assert.That(ChatText.Escape(plain), Is.SameAs(plain));
        Assert.That(ChatText.Escape(null), Is.Empty);
    }

    /// <summary>
    /// The HUD's field, once its chat wakes, takes at most 80 characters and checks each one typed or pasted.
    /// </summary>
    [Test]
    public void TheHudsField_IsKeptToAChatLine()
    {
        GameObject hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab"));
        try
        {
            ChatView view = hud.GetComponentInChildren<ChatView>(true);
            view.EnsurePrepared();
            TMP_InputField field = view.Field;
            Assert.That(field.characterLimit, Is.EqualTo(ChatText.MaxLength));
            Assert.That(field.onValidateInput("", 0, 'é'), Is.EqualTo('é'));
            Assert.That(field.onValidateInput("", 0, '一'), Is.EqualTo('\0'));
            Assert.That(field.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine), "Enter submits.");
            Assert.That(field.richText, Is.False, "Typed tags show as typed.");
        }
        finally
        {
            Object.DestroyImmediate(hud);
        }
    }
}
