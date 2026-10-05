using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="GameSave"/>, <see cref="GameSaveData"/> and <see cref="FileSaveStore"/>: saving and loading
/// the current area by scene GUID, rejecting unreadable or stale saves, and writing the save file safely.
/// </summary>
public class GameSaveTests
{
    private readonly List<Object> created = new();
    private string tempDirectory;
    private SceneDefinition clearing;
    private SceneDefinition cave;
    private GameScenes gameScenes;

    /// <summary>
    /// Builds two test area definitions and a <see cref="GameScenes"/> with the clearing as the starting area
    /// (spawn "start") and the cave as its other area.
    /// </summary>
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

    /// <summary>
    /// Destroys the created assets and deletes the test's temporary directory, if any.
    /// </summary>
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

    /// <summary>
    /// A saved area and spawn id load back as the same area definition and spawn id.
    /// </summary>
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

    /// <summary>
    /// The written save holds the current format version and the area's scene GUID, never its scene path.
    /// </summary>
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

    /// <summary>
    /// Saving a transient area that is not in <see cref="GameScenes"/>, or no area, fails without writing.
    /// </summary>
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

    /// <summary>
    /// With an empty store there is no save, and loading fails with a null area.
    /// </summary>
    [Test]
    public void TryLoadArea_WithoutASave_ReturnsFalse()
    {
        GameSave save = new(new MemorySaveStore(), gameScenes);

        Assert.That(save.HasSave, Is.False);
        Assert.That(save.TryLoadArea(out SceneDefinition area, out _), Is.False);
        Assert.That(area, Is.Null);
    }

    /// <summary>
    /// A save that is not valid JSON fails to load and logs a warning.
    /// </summary>
    [Test]
    public void TryLoadArea_CorruptFile_IsIgnoredWithAWarning()
    {
        GameSave save = new(new MemorySaveStore { Contents = "{ not json" }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("could not be read"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    /// <summary>
    /// A save written with a different format version fails to load and logs a warning.
    /// </summary>
    [Test]
    public void TryLoadArea_OtherFormatVersion_IsIgnoredWithAWarning()
    {
        string json = new GameSaveData(cave.SceneGuid, "start").ToJson()
            .Replace($"\"version\": {GameSaveData.CurrentVersion}", $"\"version\": {GameSaveData.CurrentVersion + 1}");
        GameSave save = new(new MemorySaveStore { Contents = json }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("format version"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    /// <summary>
    /// An empty JSON object reads as format version 0 and is rejected with a warning.
    /// </summary>
    [Test]
    public void TryLoadArea_EmptyObject_IsIgnoredAsAnUnknownFormat()
    {
        GameSave save = new(new MemorySaveStore { Contents = "{}" }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("format version 0"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    /// <summary>
    /// A save whose scene GUID matches no area in <see cref="GameScenes"/> fails to load and logs a warning.
    /// </summary>
    [Test]
    public void TryLoadArea_AreaNoLongerInTheBuild_IsIgnoredWithAWarning()
    {
        string json = new GameSaveData("33333333333333333333333333333333", "start").ToJson();
        GameSave save = new(new MemorySaveStore { Contents = json }, gameScenes);

        LogAssert.Expect(LogType.Warning, new Regex("not one of this build"));
        Assert.That(save.TryLoadArea(out _, out _), Is.False);
    }

    /// <summary>
    /// Without a save the starting area and spawn are returned; once an area is saved, the saved one is.
    /// </summary>
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

    /// <summary>
    /// The file store reads nothing before the first write, creates missing folders, replaces the previous save on
    /// each write and leaves no temporary file behind.
    /// </summary>
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

    /// <summary>
    /// An area saved through one file store loads through a new <see cref="GameSave"/> reading the same file.
    /// </summary>
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

    /// <summary>
    /// Picks a unique path under the system temp folder and remembers it for deletion in <see cref="TearDown"/>;
    /// the directory itself is not created here.
    /// </summary>
    private string CreateTempDirectory()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "TopDownRPG_SaveTests_" + System.Guid.NewGuid().ToString("N"));
        return tempDirectory;
    }

    /// <summary>
    /// Creates a tracked <see cref="SceneDefinition"/> pointing at <paramref name="path"/> with scene GUID
    /// <paramref name="guid"/>.
    /// </summary>
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
