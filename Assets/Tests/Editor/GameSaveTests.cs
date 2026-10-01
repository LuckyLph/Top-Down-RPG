using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class GameSaveTests
{
    private readonly List<Object> created = new();
    private string tempDirectory;
    private SceneDefinition clearing;
    private SceneDefinition cave;
    private GameScenes gameScenes;

    [SetUp]
    public void SetUp()
    {
        clearing = CreateArea("Assets/Scenes/Areas/Area_TestClearing.unity", "11111111111111111111111111111111");
        cave = CreateArea("Assets/Scenes/Areas/Area_TestCave.unity", "22222222222222222222222222222222");
        gameScenes = ScriptableObject.CreateInstance<GameScenes>();
        created.Add(gameScenes);
        SerializedObject serialized = new(gameScenes);
        serialized.FindProperty("startingArea").objectReferenceValue = clearing;
        serialized.FindProperty("startingSpawnId").stringValue = "start";
        SerializedProperty areas = serialized.FindProperty("areas");
        areas.arraySize = 1;
        areas.GetArrayElementAtIndex(0).objectReferenceValue = cave;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            Object.DestroyImmediate(instance);
        }

        created.Clear();
        if (tempDirectory != null && Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }

        tempDirectory = null;
    }

    [Test]
    public void SaveArea_ThenTryLoadArea_ReturnsTheAreaAndSpawn()
    {
        MemorySaveStore store = new();
        GameSave save = new(store, gameScenes);

        Assert.That(save.SaveArea(cave, "door_north"), Is.True);

        Assert.That(save.HasSave, Is.True);
        Assert.That(save.TryLoadArea(out SceneDefinition area, out string spawnId), Is.True);
        Assert.That(area, Is.SameAs(cave));
        Assert.That(spawnId, Is.EqualTo("door_north"));
    }

    [Test]
    public void SaveArea_StoresTheSceneGuidAndFormatVersion()
    {
        MemorySaveStore store = new();
        new GameSave(store, gameScenes).SaveArea(cave, "door_north");

        Assert.That(GameSaveData.TryParse(store.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(GameSaveData.CurrentVersion));
        Assert.That(data.AreaSceneGuid, Is.EqualTo(cave.SceneGuid));
        Assert.That(store.Contents, Does.Not.Contain(cave.ScenePath), "Saves identify areas by GUID so moving a scene keeps saves valid.");
    }

    [Test]
    public void SaveArea_AreaOutsideGameScenes_WritesNothing()
    {
        MemorySaveStore store = new();
        GameSave save = new(store, gameScenes);
        SceneDefinition transient = SceneDefinition.CreateTransient("Assets/Dev/StressTest/Area_StressTest.unity");
        created.Add(transient);

        Assert.That(save.SaveArea(transient, null), Is.False);
        Assert.That(save.SaveArea(null, null), Is.False);
        Assert.That(store.WriteCount, Is.Zero);
    }

    [Test]
    public void TryLoadArea_WithoutASave_ReturnsFalse()
    {
        GameSave save = new(new MemorySaveStore(), gameScenes);

        Assert.That(save.HasSave, Is.False);
        Assert.That(save.TryLoadArea(out SceneDefinition area, out _), Is.False);
        Assert.That(area, Is.Null);
    }

    [Test]
    public void TryLoadArea_CorruptFile_IsIgnoredWithAWarning()
    {
        GameSave save = new(new MemorySaveStore { Contents = "{ not json" }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("could not be read"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    [Test]
    public void TryLoadArea_OtherFormatVersion_IsIgnoredWithAWarning()
    {
        string json = new GameSaveData(cave.SceneGuid, "start").ToJson()
            .Replace($"\"version\": {GameSaveData.CurrentVersion}", $"\"version\": {GameSaveData.CurrentVersion + 1}");
        GameSave save = new(new MemorySaveStore { Contents = json }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("format version"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    [Test]
    public void TryLoadArea_EmptyObject_IsIgnoredAsAnUnknownFormat()
    {
        GameSave save = new(new MemorySaveStore { Contents = "{}" }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("format version 0"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    [Test]
    public void TryLoadArea_AreaNoLongerInTheBuild_IsIgnoredWithAWarning()
    {
        string json = new GameSaveData("33333333333333333333333333333333", "start").ToJson();
        GameSave save = new(new MemorySaveStore { Contents = json }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("not one of this build"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    [Test]
    public void GetSavedOrStartingArea_PrefersTheSave_AndFallsBackToTheStartingArea()
    {
        MemorySaveStore store = new();
        GameSave save = new(store, gameScenes);

        save.GetSavedOrStartingArea(out SceneDefinition area, out string spawnId);
        Assert.That(area, Is.SameAs(clearing));
        Assert.That(spawnId, Is.EqualTo("start"));

        save.SaveArea(cave, "door_north");
        save.GetSavedOrStartingArea(out area, out spawnId);
        Assert.That(area, Is.SameAs(cave));
        Assert.That(spawnId, Is.EqualTo("door_north"));
    }

    [Test]
    public void FileSaveStore_WriteThenRead_RoundTripsAndReplacesTheOldSave()
    {
        string path = Path.Combine(CreateTempDirectory(), "nested", FileSaveStore.DefaultFileName);
        FileSaveStore store = new(path);

        Assert.That(store.TryRead(out _), Is.False);
        Assert.That(store.Write("first"), Is.True);
        Assert.That(store.Write("second"), Is.True);

        Assert.That(store.TryRead(out string contents), Is.True);
        Assert.That(contents, Is.EqualTo("second"));
        Assert.That(File.Exists(path + ".tmp"), Is.False, "The temporary file is swapped in, not left behind.");
    }

    [Test]
    public void FileSaveStore_SaveSurvivesANewGameSave()
    {
        FileSaveStore store = new(Path.Combine(CreateTempDirectory(), FileSaveStore.DefaultFileName));
        new GameSave(store, gameScenes).SaveArea(cave, "door_north");

        GameSave reloaded = new(new FileSaveStore(store.FilePath), gameScenes);

        Assert.That(reloaded.TryLoadArea(out SceneDefinition area, out string spawnId), Is.True);
        Assert.That(area, Is.SameAs(cave));
        Assert.That(spawnId, Is.EqualTo("door_north"));
    }

    private string CreateTempDirectory()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "TopDownRPG_SaveTests_" + System.Guid.NewGuid().ToString("N"));
        return tempDirectory;
    }

    private SceneDefinition CreateArea(string path, string guid)
    {
        SceneDefinition definition = ScriptableObject.CreateInstance<SceneDefinition>();
        created.Add(definition);
        SerializedObject serialized = new(definition);
        serialized.FindProperty("sceneGuid").stringValue = guid;
        serialized.FindProperty("scenePath").stringValue = path;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }
}
