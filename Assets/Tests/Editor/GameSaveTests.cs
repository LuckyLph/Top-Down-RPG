using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="GameSave"/>, <see cref="GameSaveData"/>, <see cref="SessionSave"/>, <see cref="FileSaveStore"/>
/// and <see cref="InstanceFiles"/>: the version 2 save keyed by player id and world id round trips, older, newer and
/// unreadable saves are ignored with a warning and kept as a copy before the first write replaces them, the world clock
/// continues from the saved time, the save file is written safely, and a Multiplayer Play Mode virtual player keeps
/// files of its own.
/// </summary>
public class GameSaveTests
{
    private const string WorldA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WorldB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Alice = "alice-id";
    private const string Bob = "bob-id";

    private string tempDirectory;

    /// <summary>
    /// Deletes the test's temporary directory, if any.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (tempDirectory != null && Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }

        tempDirectory = null;
    }

    /// <summary>
    /// A session's players, worlds, chunk records and world clock written through <see cref="GameSave"/> load back into a
    /// new session as they were, each player under its own id.
    /// </summary>
    [Test]
    public void ASessionWritten_LoadsBackIntoANewSession_KeyedByPlayerAndWorld()
    {
        ManualClock clock = new() { Time = 50f };
        SessionSave written = new(clock);
        written.StartNew();
        clock.Advance(125f);
        written.SetPlace(Alice, WorldA, new Vector2(2100.5f, 40.25f));
        written.SetPlace(Bob, WorldB, new Vector2(4130f, 33f));
        ChunkState chunk = written.WorldOf(WorldA).Chunk(new Vector2Int(1, 0));
        chunk.RecordMobDeath("mob-1", 100d);
        chunk.RecordTerrainEdit(1, new Vector2Int(70, 12), true);
        written.WorldOf(WorldB).Chunk(Vector2Int.zero).RecordTerrainEdit(0, new Vector2Int(3, 4), false);
        MemorySaveStore store = new();

        Assert.That(new GameSave(store).Write(written.ToData()), Is.True);
        GameSave save = new(store);
        Assert.That(save.HasSave, Is.True);
        Assert.That(save.TryLoad(out GameSaveData data), Is.True);
        SessionSave loaded = new(clock);
        loaded.StartFrom(data);

        Assert.That(loaded.Time, Is.EqualTo(125d).Within(1e-6), "The world clock continues from the saved time.");
        Assert.That(loaded.TryGetPlace(Alice, out string aliceWorld, out Vector2 alicePosition), Is.True);
        Assert.That(aliceWorld, Is.EqualTo(WorldA));
        Assert.That(alicePosition, Is.EqualTo(new Vector2(2100.5f, 40.25f)));
        Assert.That(loaded.TryGetPlace(Bob, out string bobWorld, out Vector2 bobPosition), Is.True);
        Assert.That(bobWorld, Is.EqualTo(WorldB));
        Assert.That(bobPosition, Is.EqualTo(new Vector2(4130f, 33f)));
        Assert.That(loaded.TryGetPlace("someone-else", out _, out _), Is.False);
        Assert.That(loaded.TryGetWorld(WorldA, out WorldState worldA), Is.True);
        Assert.That(worldA.TryGetChunk(new Vector2Int(1, 0), out ChunkState loadedChunk), Is.True);
        Assert.That(loadedChunk.TryGetMobDeath("mob-1", out double diedAt), Is.True);
        Assert.That(diedAt, Is.EqualTo(100d));
        Assert.That(loadedChunk.TerrainEdits, Is.EqualTo(new[] { new GameSaveData.CellEdit(1, new Vector2Int(70, 12), true) }));
        Assert.That(loaded.TryGetWorld(WorldB, out WorldState worldB), Is.True);
        Assert.That(worldB.TryGetChunk(Vector2Int.zero, out ChunkState chunkB), Is.True);
        Assert.That(chunkB.TerrainEdits, Is.EqualTo(new[] { new GameSaveData.CellEdit(0, new Vector2Int(3, 4), false) }));
    }

    /// <summary>
    /// A hand-written version 2 save parses into its time, players with their waystones, and worlds with their chunk
    /// records.
    /// </summary>
    [Test]
    public void AVersion2Save_Parses()
    {
        string json = "{\"version\":2,\"time\":3600.5,"
            + "\"players\":[{\"playerId\":\"alice-id\",\"worldId\":\"" + WorldA + "\",\"position\":{\"x\":2060.5,\"y\":42.0},"
            + "\"waystones\":[{\"worldId\":\"" + WorldA + "\",\"entry\":\"start\"}]}],"
            + "\"worlds\":[{\"worldId\":\"" + WorldA + "\",\"chunks\":[{\"coords\":{\"x\":1,\"y\":0},"
            + "\"deadMobs\":[{\"id\":\"mob-1\",\"diedAt\":3500.25}],"
            + "\"terrainEdits\":[{\"layer\":1,\"cell\":{\"x\":70,\"y\":12},\"filled\":true}]}]}]}";

        Assert.That(GameSaveData.TryParse(json, out GameSaveData data), Is.True);

        Assert.That(data.Version, Is.EqualTo(2));
        Assert.That(data.Time, Is.EqualTo(3600.5d));
        Assert.That(data.Players, Has.Count.EqualTo(1));
        Assert.That(data.Players[0].PlayerId, Is.EqualTo(Alice));
        Assert.That(data.Players[0].WorldId, Is.EqualTo(WorldA));
        Assert.That(data.Players[0].Position, Is.EqualTo(new Vector2(2060.5f, 42f)));
        Assert.That(data.Players[0].Waystones, Has.Count.EqualTo(1));
        Assert.That(data.Players[0].Waystones[0].WorldId, Is.EqualTo(WorldA));
        Assert.That(data.Players[0].Waystones[0].Entry, Is.EqualTo("start"));
        Assert.That(data.Worlds, Has.Count.EqualTo(1));
        GameSaveData.Chunk chunk = data.Worlds[0].Chunks[0];
        Assert.That(chunk.Coords, Is.EqualTo(new Vector2Int(1, 0)));
        Assert.That(chunk.DeadMobs[0].Id, Is.EqualTo("mob-1"));
        Assert.That(chunk.DeadMobs[0].DiedAt, Is.EqualTo(3500.25d));
        Assert.That(chunk.TerrainEdits, Is.EqualTo(new[] { new GameSaveData.CellEdit(1, new Vector2Int(70, 12), true) }));
    }

    /// <summary>
    /// A save without players or worlds reads them as empty lists.
    /// </summary>
    [Test]
    public void AVersion2Save_WithoutListsReadsThemEmpty()
    {
        Assert.That(GameSaveData.TryParse("{\"version\":2}", out GameSaveData data), Is.True);

        Assert.That(data.Players, Is.Empty);
        Assert.That(data.Worlds, Is.Empty);
    }

    /// <summary>
    /// The written save holds the current format version, every player's waystones (none yet, so an empty list), and
    /// no record for a chunk that has nothing in it.
    /// </summary>
    [Test]
    public void TheWrittenSave_HoldsTheFormatVersion_EmptyWaystones_AndNoEmptyChunk()
    {
        SessionSave session = new(new ManualClock());
        session.SetPlace(Alice, WorldA, Vector2.one);
        session.WorldOf(WorldA).Chunk(new Vector2Int(1, 1));
        MemorySaveStore store = new();

        new GameSave(store).Write(session.ToData());

        Assert.That(GameSaveData.TryParse(store.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(GameSaveData.CurrentVersion));
        Assert.That(GameSaveData.CurrentVersion, Is.EqualTo(2));
        Assert.That(store.Contents, Does.Contain("\"waystones\": []"));
        Assert.That(data.Worlds[0].WorldId, Is.EqualTo(WorldA));
        Assert.That(data.Worlds[0].Chunks, Is.Empty, "A chunk with no record is not written.");
    }

    /// <summary>
    /// A version 1 save, which held the area the party was in, is ignored with a warning, so Continue is off.
    /// </summary>
    [Test]
    public void AVersion1Save_IsIgnoredWithAWarning()
    {
        GameSave save = new(new MemorySaveStore { Contents = "{\"version\":1,\"areaSceneGuid\":\"11111111111111111111111111111111\",\"spawnId\":\"start\"}" });

        LogAssert.Expect(LogType.Warning, new Regex("format version 1"));
        Assert.That(save.TryLoad(out GameSaveData data), Is.False);
        Assert.That(data, Is.Null);
    }

    /// <summary>
    /// A save from a newer format version is ignored with a warning.
    /// </summary>
    [Test]
    public void ANewerSave_IsIgnoredWithAWarning()
    {
        string json = new GameSaveData(0d, new GameSaveData.Player[0], new GameSaveData.World[0]).ToJson()
            .Replace($"\"version\": {GameSaveData.CurrentVersion}", $"\"version\": {GameSaveData.CurrentVersion + 1}");
        GameSave save = new(new MemorySaveStore { Contents = json });

        LogAssert.Expect(LogType.Warning, new Regex("format version"));
        Assert.That(save.TryLoad(out _), Is.False);
    }

    /// <summary>
    /// With an empty store there is no save; a file that is not JSON, or an empty object (format version 0), is
    /// ignored with a warning.
    /// </summary>
    [Test]
    public void NoSave_ACorruptFile_AndAnEmptyObject_LoadNothing()
    {
        Assert.That(new GameSave(new MemorySaveStore()).HasSave, Is.False);

        LogAssert.Expect(LogType.Warning, new Regex("could not be read"));
        Assert.That(new GameSave(new MemorySaveStore { Contents = "{ not json" }).TryLoad(out _), Is.False);

        LogAssert.Expect(LogType.Warning, new Regex("format version 0"));
        Assert.That(new GameSave(new MemorySaveStore { Contents = "{}" }).TryLoad(out _), Is.False);
    }

    /// <summary>
    /// The first write over a stored save this build cannot read (one that does not parse, or of a newer format
    /// version) keeps a copy of it first, with a warning, and the next writes keep none; a save this build reads, or
    /// nothing stored, is written over with no copy.
    /// </summary>
    [Test]
    public void WritingOverASaveThisBuildCannotRead_KeepsACopyOfItFirst()
    {
        SessionSave session = new(new ManualClock());
        session.SetPlace(Alice, WorldA, Vector2.one);
        string newer = new GameSaveData(0d, new GameSaveData.Player[0], new GameSaveData.World[0]).ToJson()
            .Replace($"\"version\": {GameSaveData.CurrentVersion}", $"\"version\": {GameSaveData.CurrentVersion + 1}");
        foreach (string unreadable in new[] { "{ not json", newer })
        {
            MemorySaveStore store = new() { Contents = unreadable };
            GameSave save = new(store);

            LogAssert.Expect(LogType.Warning, new Regex("was kept as 'copy 1'"));
            Assert.That(save.Write(session.ToData()), Is.True);
            Assert.That(store.KeptCopies, Is.EqualTo(new[] { unreadable }), "The unreadable save is kept before it is replaced.");
            Assert.That(save.Write(session.ToData()), Is.True);
            Assert.That(store.KeptCopies.Count, Is.EqualTo(1), "Writing over its own save keeps no copy.");
            Assert.That(store.WriteCount, Is.EqualTo(2));
        }

        MemorySaveStore readable = new() { Contents = session.ToData().ToJson() };
        MemorySaveStore empty = new();
        Assert.That(new GameSave(readable).Write(session.ToData()), Is.True);
        Assert.That(new GameSave(empty).Write(session.ToData()), Is.True);
        Assert.That(readable.KeptCopies, Is.Empty, "A save this build reads is written over, as a new game does.");
        Assert.That(empty.KeptCopies, Is.Empty);
    }

    /// <summary>
    /// The file store keeps a copy of the save beside it, numbered after the first, and none when there is no file.
    /// </summary>
    [Test]
    public void FileSaveStore_KeepsNumberedCopies()
    {
        string path = Path.Combine(CreateTempDirectory(), FileSaveStore.DefaultFileName);
        FileSaveStore store = new(path);
        Assert.That(store.KeepCopy(out string none), Is.True);
        Assert.That(none, Is.Null);

        store.Write("first");
        Assert.That(store.KeepCopy(out string copy), Is.True);
        store.Write("second");
        Assert.That(store.KeepCopy(out string second), Is.True);

        Assert.That(copy, Is.EqualTo(path + ".bak"));
        Assert.That(second, Is.EqualTo(path + ".bak2"));
        Assert.That(File.ReadAllText(copy), Is.EqualTo("first"));
        Assert.That(File.ReadAllText(second), Is.EqualTo("second"));
    }

    /// <summary>
    /// Places are keyed by player id: setting a player's place again replaces it and keeps the waystones it was loaded
    /// with; an empty player or world id is not recorded.
    /// </summary>
    [Test]
    public void Places_AreKeyedByPlayerId_AndKeepTheirWaystones()
    {
        GameSaveData data = new(
            0d,
            new[] { new GameSaveData.Player(Alice, WorldA, Vector2.zero, new[] { new GameSaveData.Waystone(WorldA, "start") }) },
            new GameSaveData.World[0]);
        SessionSave session = new(new ManualClock());
        session.StartFrom(data);

        session.SetPlace(Alice, WorldB, new Vector2(5f, 6f));
        session.SetPlace(Bob, WorldA, new Vector2(7f, 8f));
        session.SetPlace(string.Empty, WorldA, Vector2.zero);
        session.SetPlace("carol-id", null, Vector2.zero);

        Assert.That(session.PlayerCount, Is.EqualTo(2));
        Assert.That(session.TryGetPlace(Alice, out string world, out Vector2 position), Is.True);
        Assert.That(world, Is.EqualTo(WorldB));
        Assert.That(position, Is.EqualTo(new Vector2(5f, 6f)));
        Assert.That(session.WaystonesOf(Alice), Has.Count.EqualTo(1));
        Assert.That(session.WaystonesOf(Alice)[0].Entry, Is.EqualTo("start"));
        Assert.That(session.WaystonesOf(Bob), Is.Empty);
    }

    /// <summary>
    /// The world clock is the saved time plus the game time since the session started; a new game starts it at zero
    /// and forgets every player and world.
    /// </summary>
    [Test]
    public void TheWorldClock_ContinuesFromTheSave_AndANewGameStartsOver()
    {
        ManualClock clock = new() { Time = 1000f };
        SessionSave session = new(clock);
        session.StartFrom(new GameSaveData(
            500d,
            new[] { new GameSaveData.Player(Alice, WorldA, Vector2.zero) },
            new[] { new GameSaveData.World(WorldA, new GameSaveData.Chunk[0]) }));

        Assert.That(session.Time, Is.EqualTo(500d));
        clock.Advance(20f);
        Assert.That(session.Time, Is.EqualTo(520d));

        session.StartNew();
        Assert.That(session.Time, Is.EqualTo(0d));
        Assert.That(session.PlayerCount, Is.Zero);
        Assert.That(session.WorldCount, Is.Zero);
        clock.Advance(3f);
        Assert.That(session.Time, Is.EqualTo(3d));
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
    /// A save written through one file store loads through a new <see cref="GameSave"/> reading the same file.
    /// </summary>
    [Test]
    public void FileSaveStore_SaveSurvivesANewGameSave()
    {
        FileSaveStore store = new(Path.Combine(CreateTempDirectory(), FileSaveStore.DefaultFileName));
        SessionSave session = new(new ManualClock());
        session.SetPlace(Alice, WorldA, new Vector2(1f, 2f));
        new GameSave(store).Write(session.ToData());

        GameSave reloaded = new(new FileSaveStore(store.FilePath));

        Assert.That(reloaded.TryLoad(out GameSaveData data), Is.True);
        Assert.That(data.Players[0].PlayerId, Is.EqualTo(Alice));
        Assert.That(data.Players[0].Position, Is.EqualTo(new Vector2(1f, 2f)));
    }

    /// <summary>
    /// A Multiplayer Play Mode virtual player (<c>-name Player2</c>) keeps files of its own; the main editor
    /// (<c>Player1</c> or no name) and any other name keep the plain ones.
    /// </summary>
    [Test]
    public void InstanceFiles_GiveAVirtualPlayerItsOwnFiles()
    {
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name", "Player2" }), Is.EqualTo("save.Player2.json"));
        Assert.That(InstanceFiles.NameFor("settings.json", new[] { "-projectPath", "x", "-name", "Player4" }), Is.EqualTo("settings.Player4.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name", "Player1" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name", "../evil" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name", "PlayerX" }), Is.EqualTo("save.json"));
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
}
