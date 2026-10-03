using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class UserSettingsTests
{
    [Test]
    public void MissingFile_GivesDefaults_WithoutWriting()
    {
        MemorySaveStore store = new();

        UserSettings settings = new(store);

        Assert.That(settings.InputBindingOverrides, Is.Empty);
        Assert.That(store.WriteCount, Is.Zero);
    }

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

    [Test]
    public void MalformedFile_IsIgnoredWithAWarning()
    {
        MemorySaveStore store = new() { Contents = "not json" };
        LogAssert.Expect(LogType.Warning, new Regex("settings file could not be read"));

        UserSettings settings = new(store);

        Assert.That(settings.InputBindingOverrides, Is.Empty);
    }

    [Test]
    public void OtherFormatVersion_IsIgnoredWithAWarning()
    {
        MemorySaveStore store = new() { Contents = "{\"version\":2,\"inputBindingOverrides\":\"x\"}" };
        LogAssert.Expect(LogType.Warning, new Regex("format version 2"));

        UserSettings settings = new(store);

        Assert.That(settings.InputBindingOverrides, Is.Empty);
    }

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

    [Test]
    public void SettingsFile_IsSeparateFromTheGameSave()
    {
        Assert.That(UserSettings.FileName, Is.Not.EqualTo(FileSaveStore.DefaultFileName));
    }
}
