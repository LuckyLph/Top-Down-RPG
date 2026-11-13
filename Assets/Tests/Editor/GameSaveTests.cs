using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="GameSave"/>, <see cref="GameSaveData"/>, <see cref="SessionSave"/>, <see cref="FileSaveFolder"/>,
/// <see cref="FileSaveStore"/> and <see cref="InstanceFiles"/> (Docs/KnownGaps.md, Phase 5): a save file per session,
/// the version 4 save keyed by player id and world id round tripping with each player's weapon and terrain edits naming
/// their layers, two sessions saved side by side with Continue loading the latest, a joined client's copy of its place
/// playing on its own as a session of its own, its host gone or not (Docs/UiRevamp.md, Phase 10), every save listed
/// newest first and deleted with one copy kept (Phase 10), the save of an earlier build imported once
/// (its terrain edits dropped) or, when too old, ignored with its warning, newer and unreadable files ignored with a
/// warning and kept as a copy before anything writes over them, the world clock continuing from the saved time, the
/// files written safely, and a Multiplayer Play Mode virtual player keeping files of its own.
/// </summary>
public class GameSaveTests
{
    private const string WorldA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WorldB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Alice = "alice-id";
    private const string Bob = "bob-id";
    private const string Grass = "grass-material-guid";
    private const string HostSession = "host-session-id";

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
    /// A session's players (their places and weapons), worlds, chunk records (terrain edits naming their layer) and
    /// world clock written through <see cref="GameSave"/> load back into a new session as they were, under the session's
    /// id, each player under its own id.
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
        written.SetWeapon(Alice, "longbow-guid");
        ChunkState chunk = written.WorldOf(WorldA).Chunk(new Vector2Int(1, 0));
        chunk.RecordMobDeath("mob-1", 100d);
        chunk.RecordTerrainEdit(Grass, new Vector2Int(70, 12), true);
        written.WorldOf(WorldB).Chunk(Vector2Int.zero).RecordTerrainEdit(AreaTerrain.WallsKey, new Vector2Int(3, 4), false);
        MemorySaveFolder folder = new();

        Assert.That(new GameSave(folder, new ManualWallClock()).Write(written.ToData()), Is.True);
        Assert.That(folder.Names(), Is.EqualTo(new[] { $"session-{written.Id}.json" }), "A session's save is a file of its own, named by its id.");
        GameSave save = new(folder, new ManualWallClock());
        Assert.That(save.HasSave, Is.True);
        Assert.That(save.TryLoadLatest(out GameSaveData data), Is.True);
        SessionSave loaded = new(clock);
        loaded.StartFrom(data);

        Assert.That(loaded.Id, Is.EqualTo(written.Id), "Continuing a session keeps its id, so it saves into its own file.");
        Assert.That(loaded.Time, Is.EqualTo(125d).Within(1e-6), "The world clock continues from the saved time.");
        Assert.That(loaded.TryGetPlace(Alice, out string aliceWorld, out Vector2 alicePosition), Is.True);
        Assert.That(aliceWorld, Is.EqualTo(WorldA));
        Assert.That(alicePosition, Is.EqualTo(new Vector2(2100.5f, 40.25f)));
        Assert.That(loaded.TryGetPlace(Bob, out string bobWorld, out Vector2 bobPosition), Is.True);
        Assert.That(bobWorld, Is.EqualTo(WorldB));
        Assert.That(bobPosition, Is.EqualTo(new Vector2(4130f, 33f)));
        Assert.That(loaded.TryGetPlace("someone-else", out _, out _), Is.False);
        Assert.That(loaded.WeaponOf(Alice), Is.EqualTo("longbow-guid"), "The weapon a player holds is saved with its place.");
        Assert.That(loaded.WeaponOf(Bob), Is.Empty, "A player with no weapon recorded keeps its starting weapon.");
        Assert.That(loaded.TryGetWorld(WorldA, out WorldState worldA), Is.True);
        Assert.That(worldA.TryGetChunk(new Vector2Int(1, 0), out ChunkState loadedChunk), Is.True);
        Assert.That(loadedChunk.TryGetMobDeath("mob-1", out double diedAt), Is.True);
        Assert.That(diedAt, Is.EqualTo(100d));
        Assert.That(loadedChunk.TerrainEdits, Is.EqualTo(new[] { new GameSaveData.CellEdit(Grass, new Vector2Int(70, 12), true) }));
        Assert.That(loaded.TryGetWorld(WorldB, out WorldState worldB), Is.True);
        Assert.That(worldB.TryGetChunk(Vector2Int.zero, out ChunkState chunkB), Is.True);
        Assert.That(chunkB.TerrainEdits, Is.EqualTo(new[] { new GameSaveData.CellEdit(AreaTerrain.WallsKey, new Vector2Int(3, 4), false) }));
    }

    /// <summary>
    /// Two sessions save side by side, each into its own file, neither touching the other's; Continue loads the one
    /// saved last, by the time each file records, whatever the file names.
    /// </summary>
    [Test]
    public void TwoSessions_AreSavedSideBySide_AndContinueLoadsTheLatest()
    {
        ManualWallClock wallClock = new();
        MemorySaveFolder folder = new();
        GameSave save = new(folder, wallClock);
        SessionSave first = new(new ManualClock());
        first.StartNew();
        first.SetPlace(Alice, WorldA, new Vector2(1f, 1f));
        SessionSave second = new(new ManualClock());
        second.StartNew();
        second.SetPlace(Alice, WorldB, new Vector2(2f, 2f));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id), "Each new game is a session of its own.");

        Assert.That(save.Write(first.ToData()), Is.True);
        wallClock.Advance(10d);
        Assert.That(save.Write(second.ToData()), Is.True);

        Assert.That(folder.Names(), Has.Count.EqualTo(2));
        Assert.That(save.TryLoadLatest(out GameSaveData latest), Is.True);
        Assert.That(latest.Id, Is.EqualTo(second.Id), "The session saved last is the latest.");
        string firstFile = folder.Read(GameSave.FileNameOf(first.ToData()));
        Assert.That(GameSaveData.TryParse(firstFile, out GameSaveData firstData), Is.True);
        Assert.That(firstData.Players[0].WorldId, Is.EqualTo(WorldA), "Saving the second session left the first one's file as it was.");

        wallClock.Advance(10d);
        first.SetPlace(Alice, WorldA, new Vector2(5f, 5f));
        Assert.That(save.Write(first.ToData()), Is.True);
        Assert.That(save.TryLoadLatest(out latest), Is.True);
        Assert.That(latest.Id, Is.EqualTo(first.Id), "Saving the first session again makes it the latest.");
        Assert.That(latest.Players[0].Position, Is.EqualTo(new Vector2(5f, 5f)));
        Assert.That(folder.Names(), Has.Count.EqualTo(2), "A session's later saves replace its own file.");
    }

    /// <summary>
    /// The written save holds the current format version, the session's id, when it was saved, every player's
    /// waystones (none yet, so an empty list), and no record for a chunk that has nothing in it.
    /// </summary>
    [Test]
    public void TheWrittenSave_HoldsTheFormatVersion_TheIdAndTime_EmptyWaystones_AndNoEmptyChunk()
    {
        SessionSave session = new(new ManualClock());
        session.StartNew();
        session.SetPlace(Alice, WorldA, Vector2.one);
        session.WorldOf(WorldA).Chunk(new Vector2Int(1, 1));
        MemorySaveFolder folder = new();
        ManualWallClock wallClock = new();

        new GameSave(folder, wallClock).Write(session.ToData());

        Assert.That(GameSaveData.TryParse(folder.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(GameSaveData.CurrentVersion));
        Assert.That(GameSaveData.CurrentVersion, Is.EqualTo(4), "Terrain edits naming their layer raised the format to 4.");
        Assert.That(data.Id, Is.EqualTo(session.Id));
        Assert.That(data.SavedAtUtc(), Is.EqualTo(wallClock.UtcNow));
        Assert.That(data.IsCopy, Is.False);
        Assert.That(folder.Contents, Does.Contain("\"waystones\": []"));
        Assert.That(folder.Contents, Does.Contain("\"weapon\": \"\""), "A player without a weapon recorded saves none.");
        Assert.That(data.Worlds[0].WorldId, Is.EqualTo(WorldA));
        Assert.That(data.Worlds[0].Chunks, Is.Empty, "A chunk with no record is not written.");
    }

    /// <summary>
    /// A joined client's copy of its place is written as its host session's copy and is a save Continue loads, its host
    /// gone or not (Docs/UiRevamp.md, Phase 10); the copy says whether its host is gone, and it goes on as a session of
    /// its own under a new id, its player's place, waystones, weapon and the time kept, the copy left as it is.
    /// </summary>
    [Test]
    public void AClientsCopy_PlaysOnItsOwn_AndGoesOnAsASessionOfItsOwn()
    {
        ManualWallClock wallClock = new();
        MemorySaveFolder folder = new();
        GameSave save = new(folder, wallClock);
        GameSaveData copy = new(
            900d,
            new[] { new GameSaveData.Player(Alice, WorldA, new Vector2(7f, 8f), new[] { new GameSaveData.Waystone(WorldA, "north") }, "longbow-guid") },
            new GameSaveData.World[0]);
        copy.SetHost(HostSession, false);

        Assert.That(save.Write(copy), Is.True);
        Assert.That(folder.Names(), Is.EqualTo(new[] { $"joined-{HostSession}.json" }));
        Assert.That(save.HasSave, Is.True, "A copy whose host may still be there plays on its own too.");
        Assert.That(save.TryLoadCopy(HostSession, out GameSaveData stored), Is.True);
        Assert.That(stored.HostGone, Is.False);
        Assert.That(save.TryLoadLatest(out GameSaveData waiting), Is.True);
        Assert.That(waiting.IsCopy && !waiting.HostGone, Is.True);

        wallClock.Advance(5d);
        copy.SetHost(HostSession, true);
        Assert.That(save.Write(copy), Is.True);
        Assert.That(save.TryLoadLatest(out GameSaveData latest), Is.True);
        Assert.That(latest.IsCopy && latest.HostGone, Is.True, "The copy says its host is gone.");

        SessionSave alone = new(new ManualClock());
        alone.StartFrom(latest);
        Assert.That(alone.Id, Is.Not.Empty.And.Not.EqualTo(HostSession), "The copy goes on as a session of its own.");
        Assert.That(alone.Time, Is.EqualTo(900d), "The time of day goes on from the host's.");
        Assert.That(alone.TryGetPlace(Alice, out string world, out Vector2 position), Is.True);
        Assert.That(world, Is.EqualTo(WorldA));
        Assert.That(position, Is.EqualTo(new Vector2(7f, 8f)));
        Assert.That(alone.WaystonesOf(Alice), Has.Count.EqualTo(1));
        Assert.That(alone.WeaponOf(Alice), Is.EqualTo("longbow-guid"));

        wallClock.Advance(5d);
        Assert.That(save.Write(alone.ToData()), Is.True);
        Assert.That(folder.Names(), Has.Count.EqualTo(2), "The copy is kept beside the session going on from it.");
        Assert.That(save.TryLoadLatest(out latest), Is.True);
        Assert.That(latest.Id, Is.EqualTo(alone.Id));
    }

    /// <summary>
    /// The list holds every readable save newest first, sessions and copies alike, each with its file's name, id, time
    /// played and whether it is a copy whose host is gone; a tie goes first to the name that sorts last; a file that
    /// cannot be read is left out with its warning, and kept; <see cref="GameSave.TryLoad"/> reads a save by its name.
    /// </summary>
    [Test]
    public void List_HoldsEverySaveNewestFirst_SessionsAndCopies_WithoutUnreadableFiles()
    {
        ManualWallClock wallClock = new();
        MemorySaveFolder folder = new();
        GameSave save = new(folder, wallClock);
        Assert.That(save.List(), Is.Empty);

        Assert.That(save.Write(Session("older", 60d)), Is.True);
        wallClock.Advance(10d);
        Assert.That(save.Write(Copy("gone-host", 120d, true)), Is.True);
        wallClock.Advance(10d);
        Assert.That(save.Write(Copy("present-host", 180d, false)), Is.True);
        Assert.That(save.Write(Session("tied", 240d)), Is.True);
        folder.Open("session-broken.json").Write("{ not json");

        LogAssert.Expect(LogType.Warning, new Regex("could not be read as a save"));
        var saves = save.List();

        Assert.That(saves, Has.Count.EqualTo(4), "The unreadable file is left out.");
        Assert.That(saves[0].Name, Is.EqualTo("session-tied.json"), "A tie goes first to the name that sorts last.");
        Assert.That(saves[1].Name, Is.EqualTo(GameSave.CopyNameOf("present-host")));
        Assert.That(saves[1].IsCopy && !saves[1].HostGone, Is.True);
        Assert.That(saves[1].Time, Is.EqualTo(180d));
        Assert.That(saves[2].IsCopy && saves[2].HostGone, Is.True);
        Assert.That(saves[3].Id, Is.EqualTo("older"));
        Assert.That(saves[3].IsCopy, Is.False);
        Assert.That(saves[0].SavedAtUtc(), Is.EqualTo(saves[1].SavedAtUtc()));
        Assert.That(saves[2].SavedAtUtc(), Is.LessThan(saves[1].SavedAtUtc()));
        Assert.That(folder.Read("session-broken.json"), Is.EqualTo("{ not json"), "An unreadable file is kept.");
        Assert.That(save.TryLoad("session-older.json", out GameSaveData older), Is.True);
        Assert.That(older.Time, Is.EqualTo(60d));
        Assert.That(save.TryLoad("session-broken.json", out _), Is.False);
        Assert.That(save.TryLoad("session-none.json", out _), Is.False);
    }

    /// <summary>
    /// Deleting a save takes it out of the list, keeps one copy of it (a second delete of the same file replacing it),
    /// and makes the next save the latest; a file that is not there is not deleted.
    /// </summary>
    [Test]
    public void Delete_KeepsOneCopy_AndTheLatestMovesOn()
    {
        ManualWallClock wallClock = new();
        MemorySaveFolder folder = new();
        GameSave save = new(folder, wallClock);
        Assert.That(save.Write(Session("older", 60d)), Is.True);
        wallClock.Advance(10d);
        Assert.That(save.Write(Copy(HostSession, 120d, false)), Is.True);
        string copyName = GameSave.CopyNameOf(HostSession);
        string first = folder.Read(copyName);

        Assert.That(save.Delete(copyName), Is.True);
        Assert.That(save.List(), Has.Count.EqualTo(1));
        Assert.That(folder.DeletedCopyOf(copyName), Is.EqualTo(first));
        Assert.That(save.TryLoadLatest(out GameSaveData latest), Is.True);
        Assert.That(latest.Id, Is.EqualTo("older"), "The latest moves on to the next save.");
        Assert.That(save.Delete(copyName), Is.False, "It is gone.");

        wallClock.Advance(10d);
        Assert.That(save.Write(Copy(HostSession, 300d, true)), Is.True, "Joining that host again writes the copy anew.");
        string second = folder.Read(copyName);
        Assert.That(save.Delete(copyName), Is.True);
        Assert.That(folder.DeletedCopyOf(copyName), Is.EqualTo(second), "A second delete replaces the copy kept.");
        Assert.That(save.Delete("session-older.json"), Is.True);
        Assert.That(save.HasSave, Is.False);
    }

    /// <summary>
    /// The file folder deletes a save by moving it to <c>.bak</c> beside it, a second delete of that name replacing the
    /// copy, which the folder does not list; a file that is not there is not deleted.
    /// </summary>
    [Test]
    public void FileSaveFolder_DeletesASave_KeepingOneBakCopy()
    {
        string root = CreateTempDirectory();
        FileSaveFolder folder = new(Path.Combine(root, FileSaveFolder.DefaultFolderName), Path.Combine(root, FileSaveStore.DefaultFileName));
        Assert.That(folder.Delete("session-a.json"), Is.False);
        Assert.That(folder.Open("session-a.json").Write("first"), Is.True);
        Assert.That(folder.Open("session-b.json").Write("b"), Is.True);

        Assert.That(folder.Delete("session-a.json"), Is.True);
        string bak = Path.Combine(folder.FolderPath, "session-a.json" + FileSaveFolder.DeletedSuffix);
        Assert.That(File.ReadAllText(bak), Is.EqualTo("first"));
        Assert.That(folder.Names(), Is.EqualTo(new[] { "session-b.json" }), "The copy is not listed.");

        Assert.That(folder.Open("session-a.json").Write("second"), Is.True);
        Assert.That(folder.Delete("session-a.json"), Is.True);
        Assert.That(File.ReadAllText(bak), Is.EqualTo("second"), "A second delete replaces the copy.");
        Assert.That(Directory.GetFiles(folder.FolderPath, "session-a.json*"), Has.Length.EqualTo(1));
        Assert.That(folder.Delete("session-a.json"), Is.False);
    }

    /// <summary>
    /// The in-memory folder deletes a save the same way, keeping its text as its one copy.
    /// </summary>
    [Test]
    public void MemorySaveFolder_DeletesASave_KeepingOneCopy()
    {
        MemorySaveFolder folder = new();
        Assert.That(folder.Delete("session-a.json"), Is.False);
        folder.Open("session-a.json").Write("first");
        Assert.That(folder.Delete("session-a.json"), Is.True);
        Assert.That(folder.Names(), Is.Empty);
        Assert.That(folder.DeletedCopyOf("session-a.json"), Is.EqualTo("first"));
        folder.Open("session-a.json").Write("second");
        Assert.That(folder.Delete("session-a.json"), Is.True);
        Assert.That(folder.DeletedCopyOf("session-a.json"), Is.EqualTo("second"));
    }

    /// <summary>
    /// The single save file of an earlier build (format 3, and format 2 without weapons) is imported once as a session
    /// of its own: its time, players, waystones, weapons and dead mobs kept, its terrain edits (which named layers by
    /// index) dropped with a warning, the old file moved aside; another run imports nothing again.
    /// </summary>
    [Test]
    public void TheSaveOfAnEarlierBuild_IsImportedOnce_AsASession_WithoutItsTerrainEdits()
    {
        string json = "{\"version\":3,\"time\":3600.5,"
            + "\"players\":[{\"playerId\":\"alice-id\",\"worldId\":\"" + WorldA + "\",\"position\":{\"x\":2060.5,\"y\":42.0},"
            + "\"waystones\":[{\"worldId\":\"" + WorldA + "\",\"entry\":\"north\"}],\"weapon\":\"longbow-guid\"}],"
            + "\"worlds\":[{\"worldId\":\"" + WorldA + "\",\"chunks\":[{\"coords\":{\"x\":1,\"y\":0},"
            + "\"deadMobs\":[{\"id\":\"mob-1\",\"diedAt\":3500.25}],"
            + "\"terrainEdits\":[{\"layer\":1,\"cell\":{\"x\":70,\"y\":12},\"filled\":true}]}]}]}";
        MemorySaveFolder folder = new();
        folder.LegacyStore.Contents = json;
        GameSave save = new(folder, new ManualWallClock());

        LogAssert.Expect(LogType.Warning, new Regex("Imported the save file of an earlier build as the session .*its 1 terrain edits named layers by index and were dropped"));
        Assert.That(save.TryLoadLatest(out GameSaveData imported), Is.True);

        Assert.That(imported.Version, Is.EqualTo(GameSaveData.CurrentVersion));
        Assert.That(imported.Id, Is.Not.Empty);
        Assert.That(folder.Names(), Is.EqualTo(new[] { $"session-{imported.Id}.json" }));
        Assert.That(folder.RetiredLegacy, Is.EqualTo(json), "The old file is moved aside, not deleted.");
        Assert.That(folder.LegacyStore.Contents, Is.Null);
        Assert.That(imported.Time, Is.EqualTo(3600.5d));
        Assert.That(imported.Players[0].PlayerId, Is.EqualTo(Alice));
        Assert.That(imported.Players[0].Position, Is.EqualTo(new Vector2(2060.5f, 42f)));
        Assert.That(imported.Players[0].Waystones[0].Entry, Is.EqualTo("north"));
        Assert.That(imported.Players[0].Weapon, Is.EqualTo("longbow-guid"));
        GameSaveData.Chunk chunk = imported.Worlds[0].Chunks[0];
        Assert.That(chunk.DeadMobs[0].Id, Is.EqualTo("mob-1"));
        Assert.That(chunk.TerrainEdits, Is.Empty, "Edits naming layers by index cannot be told apart once layers move.");

        GameSave nextRun = new(folder, new ManualWallClock());
        Assert.That(nextRun.TryLoadLatest(out GameSaveData again), Is.True);
        Assert.That(again.Id, Is.EqualTo(imported.Id), "Nothing is imported twice.");
        Assert.That(folder.Names(), Has.Count.EqualTo(1));

        MemorySaveFolder older = new();
        older.LegacyStore.Contents = "{\"version\":2,\"time\":10.0,\"players\":[{\"playerId\":\"alice-id\",\"worldId\":\"" + WorldA + "\",\"position\":{\"x\":1.0,\"y\":2.0},\"waystones\":[]}],\"worlds\":[]}";
        LogAssert.Expect(LogType.Warning, new Regex("Imported the save file of an earlier build"));
        Assert.That(new GameSave(older, new ManualWallClock()).TryLoadLatest(out GameSaveData version2), Is.True);
        Assert.That(version2.Players[0].Weapon, Is.Empty, "A version 2 save's players hold their starting weapon.");
    }

    /// <summary>
    /// An old save, the earlier builds' file of version 1 (the area the party was in), is ignored with its warning and
    /// left where it is, so Continue is off; a session file of an earlier format in the folder is ignored with its warning
    /// too, once a run.
    /// </summary>
    [Test]
    public void AnOldSave_IsIgnoredWithItsWarning()
    {
        string version1 = "{\"version\":1,\"areaSceneGuid\":\"11111111111111111111111111111111\",\"spawnId\":\"start\"}";
        MemorySaveFolder folder = new();
        folder.LegacyStore.Contents = version1;
        GameSave save = new(folder, new ManualWallClock());

        LogAssert.Expect(LogType.Warning, new Regex("The save file of an earlier build has format version 1.*ignored and left as it is"));
        Assert.That(save.TryLoadLatest(out GameSaveData data), Is.False);
        Assert.That(data, Is.Null);
        Assert.That(folder.LegacyStore.Contents, Is.EqualTo(version1), "An old save this build does not import is left alone.");
        Assert.That(folder.Names(), Is.Empty);

        MemorySaveFolder sessions = new();
        sessions.Seed("{\"version\":3,\"time\":1.0,\"players\":[],\"worlds\":[]}");
        GameSave reading = new(sessions, new ManualWallClock());
        LogAssert.Expect(LogType.Warning, new Regex("has format version 3, but this build reads version 4; ignoring it"));
        Assert.That(reading.HasSave, Is.False);
        Assert.That(reading.HasSave, Is.False, "The warning is given once a run.");
    }

    /// <summary>
    /// With an empty folder there is no save; a file that is not JSON, an empty object (format version 0) or a save of a
    /// newer format is ignored with a warning, the others still read.
    /// </summary>
    [Test]
    public void NoSave_ACorruptFile_AnEmptyObject_AndANewerSave_LoadNothing()
    {
        Assert.That(new GameSave(new MemorySaveFolder(), new ManualWallClock()).HasSave, Is.False);

        MemorySaveFolder folder = new();
        folder.Seed("{ not json");
        folder.Seed("{}");
        folder.Seed(NewerSave());
        SessionSave session = new(new ManualClock());
        session.StartNew();
        session.SetPlace(Alice, WorldA, Vector2.one);
        folder.Seed(session.ToData());
        GameSave save = new(folder, new ManualWallClock());

        LogAssert.Expect(LogType.Warning, new Regex("could not be read as a save"));
        LogAssert.Expect(LogType.Warning, new Regex("format version 0"));
        LogAssert.Expect(LogType.Warning, new Regex($"format version {GameSaveData.CurrentVersion + 1}"));
        Assert.That(save.TryLoadLatest(out GameSaveData latest), Is.True);
        Assert.That(latest.Id, Is.EqualTo(session.Id), "The readable session is the latest.");
    }

    /// <summary>
    /// The first write over a stored file this build cannot read (one that does not parse, or of a newer format) keeps
    /// a copy of it first, with a warning, and the next writes keep none; a file this build reads, or none, is written
    /// over with no copy.
    /// </summary>
    [Test]
    public void WritingOverAFileThisBuildCannotRead_KeepsACopyOfItFirst()
    {
        GameSaveData copy = new(0d, new[] { new GameSaveData.Player(Alice, WorldA, Vector2.one) }, new GameSaveData.World[0]);
        copy.SetHost(HostSession, false);
        string name = GameSave.CopyNameOf(HostSession);
        foreach (string unreadable in new[] { "{ not json", NewerSave() })
        {
            MemorySaveFolder folder = new();
            folder.Open(name).Write(unreadable);
            GameSave save = new(folder, new ManualWallClock());

            LogAssert.Expect(LogType.Warning, new Regex("was kept as '.*copy 1' before saving over it"));
            Assert.That(save.Write(copy), Is.True);
            Assert.That(folder.KeptCopiesOf(name), Is.EqualTo(new[] { unreadable }), "The unreadable file is kept before it is replaced.");
            Assert.That(save.Write(copy), Is.True);
            Assert.That(folder.KeptCopiesOf(name), Has.Count.EqualTo(1), "Writing over its own save keeps no copy.");
        }

        MemorySaveFolder readable = new();
        readable.Seed(copy);
        Assert.That(new GameSave(readable, new ManualWallClock()).Write(copy), Is.True);
        Assert.That(readable.KeptCopiesOf(name), Is.Empty, "A file this build reads is written over.");
    }

    /// <summary>
    /// A save that names no session or host is refused with an error.
    /// </summary>
    [Test]
    public void ASaveNamingNoSession_IsNotWritten()
    {
        MemorySaveFolder folder = new();
        LogAssert.Expect(LogType.Error, new Regex("naming no session or host"));
        Assert.That(new GameSave(folder, new ManualWallClock()).Write(new GameSaveData(0d, new GameSaveData.Player[0], new GameSaveData.World[0])), Is.False);
        Assert.That(folder.WriteCount, Is.Zero);
    }

    /// <summary>
    /// A throwaway session (an editor boot straight into a world) is marked as one until a real session starts, new or
    /// from a save; each new session, throwaway or not, has an id of its own.
    /// </summary>
    [Test]
    public void AThrowawaySession_IsMarked_UntilARealOneStarts()
    {
        SessionSave session = new(new ManualClock());
        Assert.That(session.IsThrowaway, Is.False);

        session.StartThrowaway();
        string throwaway = session.Id;
        Assert.That(session.IsThrowaway, Is.True);
        session.StartNew();
        Assert.That(session.IsThrowaway, Is.False);
        Assert.That(session.Id, Is.Not.Empty.And.Not.EqualTo(throwaway));

        session.StartThrowaway();
        session.SetPlace(Alice, WorldA, Vector2.one);
        session.StartFrom(session.ToData());
        Assert.That(session.IsThrowaway, Is.False);
    }

    /// <summary>
    /// The save folder lists only its save files (not the copies kept beside them nor files half written), opens a
    /// file inside it, and moves the earlier builds' file aside under a numbered name once imported.
    /// </summary>
    [Test]
    public void FileSaveFolder_ListsItsSaves_AndMovesTheEarlierBuildsFileAside()
    {
        string root = CreateTempDirectory();
        string legacy = Path.Combine(root, FileSaveStore.DefaultFileName);
        FileSaveFolder folder = new(Path.Combine(root, FileSaveFolder.DefaultFolderName), legacy);
        Assert.That(folder.Names(), Is.Empty, "A folder not made yet lists nothing.");

        Assert.That(folder.Open("session-a.json").Write("a"), Is.True);
        Assert.That(folder.Open("joined-b.json").Write("b"), Is.True);
        Assert.That(folder.Open("joined-b.json").KeepCopy(out _), Is.True);
        File.WriteAllText(Path.Combine(folder.FolderPath, "session-c.json.tmp"), "half");

        Assert.That(folder.Names(), Is.EqualTo(new[] { "joined-b.json", "session-a.json" }));
        Assert.That(folder.Open("session-a.json").TryRead(out string read), Is.True);
        Assert.That(read, Is.EqualTo("a"));

        File.WriteAllText(legacy, "old");
        Assert.That(folder.Legacy.TryRead(out string old), Is.True);
        Assert.That(old, Is.EqualTo("old"));
        Assert.That(folder.RetireLegacy(out string moved), Is.True);
        Assert.That(moved, Is.EqualTo(legacy + ".imported"));
        File.WriteAllText(legacy, "older");
        Assert.That(folder.RetireLegacy(out string movedAgain), Is.True);
        Assert.That(movedAgain, Is.EqualTo(legacy + ".imported2"));
        Assert.That(File.Exists(legacy), Is.False);
        Assert.That(folder.RetireLegacy(out string none), Is.True);
        Assert.That(none, Is.Null);
    }

    /// <summary>
    /// A session's save <paramref name="id"/> played for <paramref name="time"/> s, Alice alone in it.
    /// </summary>
    private static GameSaveData Session(string id, double time)
    {
        GameSaveData data = new(time, new[] { new GameSaveData.Player(Alice, WorldA, Vector2.one) }, new GameSaveData.World[0]);
        data.SetSession(id);
        return data;
    }

    /// <summary>
    /// Alice's copy of her place in the host's session <paramref name="host"/>, played for <paramref name="time"/> s,
    /// its host gone or not as <paramref name="gone"/> says.
    /// </summary>
    private static GameSaveData Copy(string host, double time, bool gone)
    {
        GameSaveData data = new(time, new[] { new GameSaveData.Player(Alice, WorldA, Vector2.one) }, new GameSaveData.World[0]);
        data.SetHost(host, gone);
        return data;
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
    /// A save written through one file folder loads through a new <see cref="GameSave"/> reading the same folder.
    /// </summary>
    [Test]
    public void FileSaveFolder_SaveSurvivesANewGameSave()
    {
        string root = CreateTempDirectory();
        string folderPath = Path.Combine(root, FileSaveFolder.DefaultFolderName);
        string legacyPath = Path.Combine(root, FileSaveStore.DefaultFileName);
        SessionSave session = new(new ManualClock());
        session.StartNew();
        session.SetPlace(Alice, WorldA, new Vector2(1f, 2f));
        new GameSave(new FileSaveFolder(folderPath, legacyPath), new SystemWallClock()).Write(session.ToData());

        GameSave reloaded = new(new FileSaveFolder(folderPath, legacyPath), new SystemWallClock());

        Assert.That(reloaded.TryLoadLatest(out GameSaveData data), Is.True);
        Assert.That(data.Id, Is.EqualTo(session.Id));
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
        Assert.That(InstanceFiles.NameFor("Saves", new[] { "Unity.exe", "-name", "Player2" }), Is.EqualTo("Saves.Player2"));
        Assert.That(InstanceFiles.NameFor("settings.json", new[] { "-projectPath", "x", "-name", "Player4" }), Is.EqualTo("settings.Player4.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name", "Player1" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name", "../evil" }), Is.EqualTo("save.json"));
        Assert.That(InstanceFiles.NameFor("save.json", new[] { "Unity.exe", "-name", "PlayerX" }), Is.EqualTo("save.json"));
    }

    /// <summary>
    /// A session's save in a format newer than this build's.
    /// </summary>
    private static string NewerSave()
    {
        GameSaveData data = new(0d, new GameSaveData.Player[0], new GameSaveData.World[0]);
        data.SetSession("newer");
        return data.ToJson().Replace($"\"version\": {GameSaveData.CurrentVersion}", $"\"version\": {GameSaveData.CurrentVersion + 1}");
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
