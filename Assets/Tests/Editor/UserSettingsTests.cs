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
    /// The settings file name differs from the game save's default file name.
    /// </summary>
    [Test]
    public void SettingsFile_IsSeparateFromTheGameSave()
    {
        Assert.That(UserSettings.FileName, Is.Not.EqualTo(FileSaveStore.DefaultFileName));
    }
}
