using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldState"/> and <see cref="ChunkState"/> (Docs/WorldStreaming.md: Persistence): the records
/// round trip through save data, a killed mob stays dead until its respawn delay has passed (or for good when it never
/// respawns) and is forgotten once it may come back, and terrain edits keep the last write per layer and cell.
/// </summary>
public class WorldStateTests
{
    private const string WorldId = "0123456789abcdef0123456789abcdef";

    /// <summary>
    /// A world's chunk records (killed mobs with their death times, terrain edits) come back from its save data as
    /// they were, chunk by chunk.
    /// </summary>
    [Test]
    public void AWorldState_RoundTripsThroughSaveData()
    {
        WorldState state = new(WorldId);
        ChunkState west = state.Chunk(Vector2Int.zero);
        west.RecordMobDeath("weasel-a", 12.5d);
        west.RecordMobDeath("weasel-b", 40d);
        west.RecordTerrainEdit(0, new Vector2Int(5, 6), false);
        ChunkState east = state.Chunk(new Vector2Int(1, 0));
        east.RecordTerrainEdit(1, new Vector2Int(70, 6), true);

        WorldState loaded = WorldState.FromData(state.ToData());

        Assert.That(loaded.WorldId, Is.EqualTo(WorldId));
        Assert.That(loaded.ChunkCount, Is.EqualTo(2));
        Assert.That(loaded.TryGetChunk(Vector2Int.zero, out ChunkState loadedWest), Is.True);
        Assert.That(loadedWest.DeadMobCount, Is.EqualTo(2));
        Assert.That(loadedWest.TryGetMobDeath("weasel-a", out double a) && a == 12.5d, Is.True);
        Assert.That(loadedWest.TryGetMobDeath("weasel-b", out double b) && b == 40d, Is.True);
        Assert.That(loadedWest.TerrainEdits, Is.EqualTo(new[] { new GameSaveData.CellEdit(0, new Vector2Int(5, 6), false) }));
        Assert.That(loaded.TryGetChunk(new Vector2Int(1, 0), out ChunkState loadedEast), Is.True);
        Assert.That(loadedEast.DeadMobCount, Is.Zero);
        Assert.That(loadedEast.TerrainEdits, Is.EqualTo(new[] { new GameSaveData.CellEdit(1, new Vector2Int(70, 6), true) }));
    }

    /// <summary>
    /// A chunk's record is made on first use and a chunk with nothing recorded is left out of the save data.
    /// </summary>
    [Test]
    public void ChunkRecords_AreMadeOnUse_AndEmptyOnesAreNotSaved()
    {
        WorldState state = new(WorldId);

        Assert.That(state.TryGetChunk(Vector2Int.one, out _), Is.False);
        ChunkState chunk = state.Chunk(Vector2Int.one);
        Assert.That(state.Chunk(Vector2Int.one), Is.SameAs(chunk));
        Assert.That(chunk.IsEmpty, Is.True);
        Assert.That(state.ToData().Chunks, Is.Empty);

        chunk.RecordMobDeath("weasel", 1d);
        Assert.That(state.ToData().Chunks, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// A killed mob stays dead while less than its respawn delay has passed; once it has, the death is forgotten so the
    /// mob spawns, and it stays forgotten.
    /// </summary>
    [Test]
    public void AKilledMob_StaysDeadUntilItsDelayHasPassed_ThenIsForgotten()
    {
        ChunkState chunk = new(Vector2Int.zero);
        chunk.RecordMobDeath("weasel", 100d);

        Assert.That(chunk.StaysDead("weasel", 100d, true, 300f), Is.True, "Just killed.");
        Assert.That(chunk.StaysDead("weasel", 399.9d, true, 300f), Is.True, "Its delay has not passed yet.");
        Assert.That(chunk.TryGetMobDeath("weasel", out _), Is.True, "A mob still dead keeps its record.");
        Assert.That(chunk.StaysDead("weasel", 400d, true, 300f), Is.False, "Its delay has passed.");
        Assert.That(chunk.TryGetMobDeath("weasel", out _), Is.False, "A mob that comes back is forgotten.");
        Assert.That(chunk.StaysDead("weasel", 400d, true, 300f), Is.False);
    }

    /// <summary>
    /// A mob that never respawns stays dead however long ago it died; a mob with no recorded death never stays dead;
    /// a zero delay brings a mob back at the next load.
    /// </summary>
    [Test]
    public void TheRespawnRules_ForBossesUnkilledMobsAndNoDelay()
    {
        ChunkState chunk = new(Vector2Int.zero);
        chunk.RecordMobDeath("boss", 0d);
        chunk.RecordMobDeath("quick", 50d);

        Assert.That(chunk.StaysDead("boss", 1e9d, false, 0f), Is.True, "A mob that never respawns stays dead.");
        Assert.That(chunk.StaysDead("weasel", 0d, true, 300f), Is.False, "A mob that never died spawns.");
        Assert.That(chunk.StaysDead(null, 0d, true, 300f), Is.False);
        Assert.That(chunk.StaysDead("quick", 50d, true, 0f), Is.False, "No delay: back as soon as its chunk loads again.");
    }

    /// <summary>
    /// A mob killed again replaces its earlier death time.
    /// </summary>
    [Test]
    public void AMobKilledAgain_KeepsItsLatestDeath()
    {
        ChunkState chunk = new(Vector2Int.zero);
        chunk.RecordMobDeath("weasel", 10d);
        chunk.RecordMobDeath("weasel", 500d);
        chunk.RecordMobDeath(string.Empty, 1d);

        Assert.That(chunk.DeadMobCount, Is.EqualTo(1));
        Assert.That(chunk.StaysDead("weasel", 700d, true, 300f), Is.True, "The delay counts from the latest death.");
    }

    /// <summary>
    /// Terrain edits keep the last write per layer and cell, in place, and different layers of one cell stay apart.
    /// </summary>
    [Test]
    public void TerrainEdits_KeepTheLastWritePerLayerAndCell()
    {
        ChunkState chunk = new(Vector2Int.zero);
        chunk.RecordTerrainEdit(0, new Vector2Int(1, 1), false);
        chunk.RecordTerrainEdit(1, new Vector2Int(1, 1), true);
        chunk.RecordTerrainEdit(0, new Vector2Int(2, 1), false);
        chunk.RecordTerrainEdit(0, new Vector2Int(1, 1), true);

        Assert.That(chunk.TerrainEdits, Is.EqualTo(new[]
        {
            new GameSaveData.CellEdit(0, new Vector2Int(1, 1), true),
            new GameSaveData.CellEdit(1, new Vector2Int(1, 1), true),
            new GameSaveData.CellEdit(0, new Vector2Int(2, 1), false),
        }));
    }
}
