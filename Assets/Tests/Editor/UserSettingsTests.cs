using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="UserSettings"/>: loading the local settings file through an in-memory store, saving
/// changes, handling malformed, older and newer files, and the install's player id.
/// </summary>
public class UserSettingsTests
{
    /// <summary>
    /// Without a settings file the settings start at their defaults and nothing is written.
    /// </summary>
    [Test]
    public void MissingFile_GivesDefaults_WithoutWriting()
    {
        MemorySaveStore store = new();

        UserSettings settings = new(store);

        Assert.That(settings.InputBindingOverrides, Is.Empty);
        Assert.That(store.WriteCount, Is.Zero);
    }

    /// <summary>
    /// Changing the binding overrides writes the file once at the current format version, and a new settings
    /// object over the same store reads them back.
    /// </summary>
    [Test]
    public void Overrides_AreWrittenOnChange_AndReadBack()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);

        Assert.That(settings.SetInputBindingOverrides("{\"bindings\":[]}"), Is.True);

        Assert.That(store.WriteCount, Is.EqualTo(1));
        Assert.That(UserSettingsData.TryParse(store.Contents, out UserSettingsData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(UserSettingsData.CurrentVersion));
        Assert.That(new UserSettings(store).InputBindingOverrides, Is.EqualTo("{\"bindings\":[]}"));
    }

    /// <summary>
    /// A file that is not valid settings JSON logs a warning and leaves the defaults in place.
    /// </summary>
    [Test]
    public void MalformedFile_IsIgnoredWithAWarning()
    {
        MemorySaveStore store = new() { Contents = "not json" };
        LogAssert.Expect(LogType.Warning, new Regex("settings file could not be read"));

        UserSettings settings = new(store);

        Assert.That(settings.InputBindingOverrides, Is.Empty);
    }

    /// <summary>
    /// A file with a different format version logs a warning naming that version and its contents are not
    /// loaded.
    /// </summary>
    [Test]
    public void OtherFormatVersion_IsIgnoredWithAWarning()
    {
        MemorySaveStore store = new() { Contents = "{\"version\":2,\"inputBindingOverrides\":\"x\"}" };
        LogAssert.Expect(LogType.Warning, new Regex("format version 2"));

        UserSettings settings = new(store);

        Assert.That(settings.InputBindingOverrides, Is.Empty);
    }

    /// <summary>
    /// A file from a newer format version makes the settings read-only: changes still apply for the session but
    /// are never written, so the newer file survives untouched.
    /// </summary>
    [Test]
    public void AFileFromANewerBuild_IsNeverOverwritten()
    {
        string newer = "{\"version\":2,\"inputBindingOverrides\":\"x\"}";
        MemorySaveStore store = new() { Contents = newer };
        LogAssert.Expect(LogType.Warning, new Regex("newer build"));

        UserSettings settings = new(store);

        Assert.That(settings.IsReadOnly, Is.True);
        Assert.That(settings.InputBindingOverrides, Is.Empty);
        Assert.That(settings.SetInputBindingOverrides("y"), Is.False);
        Assert.That(settings.InputBindingOverrides, Is.EqualTo("y"), "The change still applies for this session.");
        Assert.That(store.WriteCount, Is.Zero);
        Assert.That(store.Contents, Is.EqualTo(newer));
    }

    /// <summary>
    /// A file from an older format version is ignored but stays writable, so the next change replaces it.
    /// </summary>
    [Test]
    public void AFileFromAnOlderFormat_IsReplacedOnTheNextChange()
    {
        MemorySaveStore store = new() { Contents = "{\"version\":0,\"inputBindingOverrides\":\"x\"}" };
        LogAssert.Expect(LogType.Warning, new Regex("format version 0"));

        UserSettings settings = new(store);

        Assert.That(settings.IsReadOnly, Is.False);
        Assert.That(settings.SetInputBindingOverrides("y"), Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(1));
    }

    /// <summary>
    /// The player id is made the first time it is asked for, written once with the bindings kept, and read back by the
    /// next settings over the same file; a file from before player ids gets one the same way.
    /// </summary>
    [Test]
    public void PlayerId_IsMadeOnce_AndKeptInTheFile()
    {
        MemorySaveStore store = new() { Contents = "{\"version\":1,\"inputBindingOverrides\":\"x\"}" };
        UserSettings settings = new(store);
        Assert.That(store.WriteCount, Is.Zero, "Nothing is written until the id is needed.");

        string id = settings.PlayerId;

        Assert.That(System.Guid.TryParse(id, out _), Is.True, id);
        Assert.That(settings.PlayerId, Is.EqualTo(id));
        Assert.That(store.WriteCount, Is.EqualTo(1));
        UserSettings reread = new(store);
        Assert.That(reread.PlayerId, Is.EqualTo(id));
        Assert.That(reread.InputBindingOverrides, Is.EqualTo("x"), "Writing the id keeps the bindings.");
        Assert.That(store.WriteCount, Is.EqualTo(1));
    }

    /// <summary>
    /// With a file from a newer build the player id is made for the session only and never written.
    /// </summary>
    [Test]
    public void PlayerId_WithAFileFromANewerBuild_LastsTheSessionOnly()
    {
        MemorySaveStore store = new() { Contents = "{\"version\":2}" };
        LogAssert.Expect(LogType.Warning, new Regex("newer build"));
        UserSettings settings = new(store);

        Assert.That(settings.PlayerId, Is.Not.Empty);
        Assert.That(store.WriteCount, Is.Zero);
    }

    /// <summary>
    /// The UI size step (Docs/DesignSystem.md, Phase 3) is Normal by default and in a file from before it, kept in the
    /// file once set, raises its change once per change, and is never below Normal.
    /// </summary>
    [Test]
    public void TheUiSizeStep_IsKeptInTheFile()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        int changes = 0;
        settings.UiSizeStepChanged += () => changes++;
        Assert.That(settings.UiSizeStep, Is.Zero);

        Assert.That(settings.SetUiSizeStep(2), Is.True);
        Assert.That(settings.SetUiSizeStep(2), Is.False);
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(new UserSettings(store).UiSizeStep, Is.EqualTo(2));

        Assert.That(settings.SetUiSizeStep(-3), Is.True);
        Assert.That(settings.UiSizeStep, Is.Zero);
        Assert.That(new UserSettings(store).UiSizeStep, Is.Zero);

        Assert.That(UserSettingsData.TryParse("{\"version\":1,\"characterLook\":\"Bandit\"}", out UserSettingsData old), Is.True);
        Assert.That(old.UiSizeStep, Is.Zero);
        Assert.That(new UserSettings(new MemorySaveStore { Contents = "{\"version\":1,\"characterLook\":\"Bandit\"}" }).UiSizeStep, Is.Zero);
    }

    /// <summary>
    /// Reduce flashing and reduce motion (Docs/DesignSystem.md, Phase 6) are off by default and in a file from before
    /// them; changing either writes the file at once, raises its own change once per change and leaves the other alone.
    /// </summary>
    [Test]
    public void TheReducePreferences_AreOffByDefault_AndChangingOneWritesTheFile()
    {
        const string before = "{\"version\":1,\"characterLook\":\"Bandit\",\"uiSizeStep\":1}";
        Assert.That(UserSettingsData.TryParse(before, out UserSettingsData old), Is.True);
        Assert.That(old.ReduceFlashing, Is.False);
        Assert.That(old.ReduceMotion, Is.False);
        UserSettings fromBefore = new(new MemorySaveStore { Contents = before });
        Assert.That(fromBefore.ReduceFlashing, Is.False);
        Assert.That(fromBefore.ReduceMotion, Is.False);
        Assert.That(fromBefore.UiSizeStep, Is.EqualTo(1));

        MemorySaveStore store = new();
        UserSettings settings = new(store);
        int flashingChanges = 0;
        int motionChanges = 0;
        settings.ReduceFlashingChanged += () => flashingChanges++;
        settings.ReduceMotionChanged += () => motionChanges++;
        Assert.That(settings.ReduceFlashing, Is.False);
        Assert.That(settings.ReduceMotion, Is.False);

        Assert.That(settings.SetReduceFlashing(true), Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(1));
        Assert.That(settings.SetReduceFlashing(true), Is.False);
        Assert.That(store.WriteCount, Is.EqualTo(1), "An unchanged value writes nothing.");
        Assert.That(flashingChanges, Is.EqualTo(1));
        Assert.That(motionChanges, Is.Zero);
        UserSettings reread = new(store);
        Assert.That(reread.ReduceFlashing, Is.True);
        Assert.That(reread.ReduceMotion, Is.False);

        Assert.That(settings.SetReduceMotion(true), Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(2));
        Assert.That(motionChanges, Is.EqualTo(1));
        Assert.That(flashingChanges, Is.EqualTo(1));
        Assert.That(new UserSettings(store).ReduceMotion, Is.True);

        Assert.That(settings.SetReduceFlashing(false), Is.True);
        Assert.That(new UserSettings(store).ReduceFlashing, Is.False);
        Assert.That(new UserSettings(store).ReduceMotion, Is.True);
        Assert.That(flashingChanges, Is.EqualTo(2));
    }

    /// <summary>
    /// Allies' popups (Docs/UiRevamp.md, Phase 3) show by default, in a file from before them and for a value this build
    /// does not know; changing them writes the file at once and raises their change once per change.
    /// </summary>
    [Test]
    public void AlliesPopups_ShowByDefault_AndChangingThemWritesTheFile()
    {
        Assert.That(new UserSettings(new MemorySaveStore { Contents = "{\"version\":1,\"reduceMotion\":true}" }).AllyPopups, Is.EqualTo(AllyPopups.Shown));
        Assert.That(new UserSettings(new MemorySaveStore { Contents = "{\"version\":1,\"allyPopups\":7}" }).AllyPopups, Is.EqualTo(AllyPopups.Shown));
        Assert.That(new UserSettings(new MemorySaveStore { Contents = "{\"version\":1,\"allyPopups\":-1}" }).AllyPopups, Is.EqualTo(AllyPopups.Shown));

        MemorySaveStore store = new();
        UserSettings settings = new(store);
        int changes = 0;
        settings.AllyPopupsChanged += () => changes++;
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Shown));

        Assert.That(settings.SetAllyPopups(AllyPopups.Smaller), Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(1));
        Assert.That(settings.SetAllyPopups(AllyPopups.Smaller), Is.False);
        Assert.That(store.WriteCount, Is.EqualTo(1), "An unchanged value writes nothing.");
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(new UserSettings(store).AllyPopups, Is.EqualTo(AllyPopups.Smaller));

        Assert.That(settings.SetAllyPopups(AllyPopups.Hidden), Is.True);
        Assert.That(new UserSettings(store).AllyPopups, Is.EqualTo(AllyPopups.Hidden));
        Assert.That(new UserSettings(store).ReduceMotion, Is.False, "The other preferences are left alone.");
        Assert.That(changes, Is.EqualTo(2));
    }

    /// <summary>
    /// The chat (Docs/UiRevamp.md, Phase 12) shows no timestamps and its window stands in its place by default, in a
    /// file from before them and for a corner this build does not know; turning timestamps on and moving the window
    /// write the file at once, raise their change once per change, and read back; a place never moved puts the window
    /// back.
    /// </summary>
    [Test]
    public void TheChatsPreferences_AreOffAndInPlaceByDefault_AndChangingThemWritesTheFile()
    {
        UserSettings old = new(new MemorySaveStore { Contents = "{\"version\":1,\"reduceMotion\":true}" });
        Assert.That(old.ChatTimestamps, Is.False);
        Assert.That(old.ChatWindow.IsMoved, Is.False);
        Assert.That(new UserSettings(new MemorySaveStore { Contents = "{\"version\":1,\"chatWindow\":{\"corner\":9,\"x\":1,\"y\":1,\"width\":100,\"height\":60}}" }).ChatWindow.IsMoved, Is.False);

        MemorySaveStore store = new();
        UserSettings settings = new(store);
        int stampChanges = 0;
        int windowChanges = 0;
        settings.ChatTimestampsChanged += () => stampChanges++;
        settings.ChatWindowChanged += () => windowChanges++;

        Assert.That(settings.SetChatTimestamps(true), Is.True);
        Assert.That(settings.SetChatTimestamps(true), Is.False);
        Assert.That(store.WriteCount, Is.EqualTo(1));
        Assert.That(stampChanges, Is.EqualTo(1));

        ChatWindowPlace moved = new(ScreenCorner.BottomRight, new UnityEngine.Vector2Int(8, 80), new UnityEngine.Vector2Int(200, 100));
        Assert.That(settings.SetChatWindow(moved), Is.True);
        Assert.That(settings.SetChatWindow(moved), Is.False, "An unchanged place writes nothing.");
        Assert.That(store.WriteCount, Is.EqualTo(2));
        Assert.That(windowChanges, Is.EqualTo(1));

        UserSettings read = new(store);
        Assert.That(read.ChatTimestamps, Is.True);
        Assert.That(read.ChatWindow, Is.EqualTo(moved));
        Assert.That(read.AllyPopups, Is.EqualTo(AllyPopups.Shown), "The other preferences are left alone.");

        Assert.That(settings.SetChatWindow(new ChatWindowPlace(ScreenCorner.None, new UnityEngine.Vector2Int(3, 3), new UnityEngine.Vector2Int(100, 60))), Is.True);
        Assert.That(settings.ChatWindow.IsMoved, Is.False, "A place never moved puts the window back.");
        Assert.That(new UserSettings(store).ChatWindow.IsMoved, Is.False);
        Assert.That(windowChanges, Is.EqualTo(2));
    }

    /// <summary>
    /// The Character screen's Reset all (Docs/UiRevamp.md, Phase 7) forgets the look, every part and every colour in one
    /// write, raising each change once, and reads back as the defaults; with nothing picked it writes nothing.
    /// </summary>
    [Test]
    public void ResetCharacter_ForgetsEveryPickInOneWrite()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        Assert.That(settings.ResetCharacter(), Is.False);
        Assert.That(store.WriteCount, Is.Zero);
        settings.SetCharacterLook("Bandit");
        settings.SetCharacterPart("Hair", "Long");
        settings.SetCharacterPart("Cape", "Cape");
        settings.SetCharacterColour("Tunic", "Red");
        int writes = store.WriteCount;
        int looks = 0;
        int parts = 0;
        int colours = 0;
        settings.CharacterLookChanged += () => looks++;
        settings.CharacterPartsChanged += () => parts++;
        settings.CharacterColoursChanged += () => colours++;

        Assert.That(settings.ResetCharacter(), Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(writes + 1));
        Assert.That((looks, parts, colours), Is.EqualTo((1, 1, 1)));
        UserSettings reread = new(store);
        Assert.That(reread.CharacterLook, Is.Empty);
        Assert.That(reread.CharacterPart("Hair"), Is.Empty);
        Assert.That(reread.CharacterPart("Cape"), Is.Empty);
        Assert.That(reread.CharacterColour("Tunic"), Is.Empty);
    }

    /// <summary>
    /// The options screen's Reset accessibility and Reset interface (Docs/UiRevamp.md, Phase 8) each put their own
    /// options back in one write, raising each change once and leaving the other category's alone; with nothing to put
    /// back they write nothing.
    /// </summary>
    [Test]
    public void ResetAccessibilityAndInterface_PutBackTheirOwnOptionsInOneWrite()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        Assert.That(settings.ResetAccessibility(), Is.False);
        Assert.That(settings.ResetInterface(), Is.False);
        Assert.That(store.WriteCount, Is.Zero);
        settings.SetUiSizeStep(2);
        settings.SetReduceFlashing(true);
        settings.SetReduceMotion(true);
        settings.SetAllyPopups(AllyPopups.Hidden);
        settings.SetChatTimestamps(true);
        settings.SetChatWindow(new ChatWindowPlace(ScreenCorner.TopRight, new Vector2Int(8, 8), new Vector2Int(160, 100)));
        int changes = 0;
        settings.UiSizeStepChanged += () => changes++;
        settings.ReduceFlashingChanged += () => changes++;
        settings.ReduceMotionChanged += () => changes++;
        settings.AllyPopupsChanged += () => changes += 10;
        settings.ChatTimestampsChanged += () => changes += 10;
        settings.ChatWindowChanged += () => changes += 10;

        int writes = store.WriteCount;
        Assert.That(settings.ResetAccessibility(), Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(writes + 1));
        Assert.That(changes, Is.EqualTo(3), "Each accessibility option changed once, no interface option.");
        UserSettings reread = new(store);
        Assert.That(reread.UiSizeStep, Is.Zero);
        Assert.That(reread.ReduceFlashing, Is.False);
        Assert.That(reread.ReduceMotion, Is.False);
        Assert.That(reread.AllyPopups, Is.EqualTo(AllyPopups.Hidden));
        Assert.That(reread.ChatTimestamps, Is.True);
        Assert.That(reread.ChatWindow.IsMoved, Is.True);

        Assert.That(settings.ResetInterface(), Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(writes + 2));
        Assert.That(changes, Is.EqualTo(33));
        reread = new UserSettings(store);
        Assert.That(reread.AllyPopups, Is.EqualTo(AllyPopups.Shown));
        Assert.That(reread.ChatTimestamps, Is.False);
        Assert.That(reread.ChatWindow.IsMoved, Is.False);
    }

    /// <summary>
    /// The recent hosts (Docs/UiRevamp.md, Phase 9) are empty by default and in a file from before there were any; each
    /// host joined goes first, once whatever its case, the oldest drops past five, and each change is one write read back
    /// by the next settings.
    /// </summary>
    [Test]
    public void RecentHosts_NewestFirst_FiveAtMost_AndEmptyFromAnOlderFile()
    {
        Assert.That(new UserSettings(new MemorySaveStore { Contents = "{\"version\":1,\"inputBindingOverrides\":\"\"}" }).RecentHosts, Is.Empty);
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        Assert.That(settings.RecentHosts, Is.Empty);

        foreach (string host in new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3", "10.0.0.4", "10.0.0.5" })
        {
            Assert.That(settings.AddRecentHost(host), Is.True);
        }

        Assert.That(settings.RecentHosts, Is.EqualTo(new[] { "10.0.0.5", "10.0.0.4", "10.0.0.3", "10.0.0.2", "10.0.0.1" }));
        int writes = store.WriteCount;
        settings.AddRecentHost("10.0.0.3");
        Assert.That(settings.RecentHosts, Is.EqualTo(new[] { "10.0.0.3", "10.0.0.5", "10.0.0.4", "10.0.0.2", "10.0.0.1" }), "A host joined again goes first, once.");
        Assert.That(store.WriteCount, Is.EqualTo(writes + 1));
        settings.AddRecentHost("ana-desktop:7778");
        settings.AddRecentHost("ANA-desktop:7778");
        Assert.That(settings.RecentHosts, Is.EqualTo(new[] { "ANA-desktop:7778", "10.0.0.3", "10.0.0.5", "10.0.0.4", "10.0.0.2" }), "Five at most, the oldest dropped, a host's case ignored.");
        Assert.That(settings.AddRecentHost("  "), Is.False, "An empty host changes nothing.");

        Assert.That(new UserSettings(store).RecentHosts, Is.EqualTo(settings.RecentHosts));
        UserSettings crowded = new(new MemorySaveStore { Contents = "{\"version\":1,\"recentHosts\":[\"a\",\"b\",\"A\",\"\",\"c\",\"d\",\"e\",\"f\"]}" });
        Assert.That(crowded.RecentHosts, Is.EqualTo(new[] { "a", "b", "c", "d", "e" }), "A file holding more, repeats or blanks reads five distinct hosts.");
    }

    /// <summary>
    /// The first run is not over in a new file; marking it over writes the file once and reads back, and marking it again
    /// writes nothing (Docs/UiRevamp.md, Phase 11).
    /// </summary>
    [Test]
    public void TheFirstRun_IsNotOverInANewFile_AndMarkingItOverWritesOnce()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        Assert.That(settings.FirstRunDone, Is.False);

        Assert.That(settings.SetFirstRunDone(), Is.True);
        Assert.That(settings.FirstRunDone, Is.True);
        Assert.That(store.WriteCount, Is.EqualTo(1));
        Assert.That(settings.SetFirstRunDone(), Is.False);
        Assert.That(store.WriteCount, Is.EqualTo(1));
        Assert.That(new UserSettings(store).FirstRunDone, Is.True);
    }

    /// <summary>
    /// A file from before the flag counts as past the first run when it holds a choice the player made (key bindings, a
    /// look, a colour or part pick, a display or interface preference), and not when it holds only the player id or
    /// recent hosts.
    /// </summary>
    [Test]
    public void AnOlderFile_IsPastTheFirstRun_WhenItHoldsAChoice()
    {
        string[] withChoices =
        {
            new UserSettingsData("{\"bindings\":[]}", "id", null).ToJson(),
            new UserSettingsData(null, "id", "Bandit").ToJson(),
            new UserSettingsData(null, "id", null, new[] { new UserColourPick("Tunic", "Red") }).ToJson(),
            new UserSettingsData(null, "id", null, null, new[] { new UserPartPick("Cape", "Long") }).ToJson(),
            new UserSettingsData(null, "id", null, sizeStep: 1).ToJson(),
            new UserSettingsData(null, "id", null, lessFlashing: true).ToJson(),
            new UserSettingsData(null, "id", null, allies: AllyPopups.Hidden).ToJson(),
            new UserSettingsData(null, "id", null, timestamps: true).ToJson(),
        };
        foreach (string json in withChoices)
        {
            Assert.That(new UserSettings(new MemorySaveStore { Contents = json }).FirstRunDone, Is.True, json);
        }

        string plain = new UserSettingsData(null, "id", null, hosts: new[] { "192.168.1.20" }).ToJson();
        Assert.That(new UserSettings(new MemorySaveStore { Contents = plain }).FirstRunDone, Is.False);
    }

    /// <summary>
    /// The settings file name differs from the game save's default file name.
    /// </summary>
    [Test]
    public void SettingsFile_IsSeparateFromTheGameSave()
    {
        Assert.That(UserSettings.FileName, Is.Not.EqualTo(FileSaveStore.DefaultFileName));
    }
}
