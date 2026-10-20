using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for where players start and what place is saved for them (Docs/WorldStreaming.md: Player identity,
/// Persistence): <see cref="PlayerStarts"/> puts a player the session knows by its id at its saved place, and anyone
/// else, or a player saved somewhere that no longer exists, at the starting world's entry; the place
/// <see cref="GameSaveRecorder"/> saves is where a player stands, or the entry of its trip until it gets there.
/// </summary>
public class PlayerStartsTests
{
    private readonly List<Object> created = new();
    private WorldDefinition home;
    private WorldDefinition away;
    private WorldDefinition unlisted;
    private GameScenes gameScenes;
    private SessionSave sessionSave;
    private PlayerStarts starts;

    /// <summary>
    /// Builds a listed starting world at (2048, 0) with a <c>start</c> entry, a listed 2 x 1 world at (4096, 0) whose
    /// second chunk is missing, a world the game scenes do not list, and the rule over an empty session.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        home = MakeWorld("Home", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero }, new WorldDefinition.Entry("start", new Vector2(10.5f, 12.5f), true));
        away = MakeWorld("Away", new Vector2Int(4096, 0), new Vector2Int(2, 1), new[] { Vector2Int.zero }, new WorldDefinition.Entry("start", new Vector2(32f, 32f), true));
        unlisted = MakeWorld("Unlisted", new Vector2Int(6144, 0), Vector2Int.one, new[] { Vector2Int.zero });
        gameScenes = ScriptableObject.CreateInstance<GameScenes>();
        created.Add(gameScenes);
        SerializedObject data = new(gameScenes);
        SerializedProperty listed = data.FindProperty("worlds");
        listed.arraySize = 2;
        listed.GetArrayElementAtIndex(0).objectReferenceValue = home;
        listed.GetArrayElementAtIndex(1).objectReferenceValue = away;
        data.FindProperty("startingWorld").objectReferenceValue = home;
        data.FindProperty("startingWorldEntry").stringValue = "start";
        data.ApplyModifiedPropertiesWithoutUndo();
        sessionSave = new SessionSave(new ManualClock());
        starts = new PlayerStarts(gameScenes, sessionSave);
    }

    /// <summary>
    /// Destroys what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A player the session knows starts at its saved world and position; another player starts at the starting
    /// world's entry.
    /// </summary>
    [Test]
    public void AKnownPlayer_StartsAtItsSavedPlace_AndAnUnknownOneAtTheStartingEntry()
    {
        Vector2 saved = away.ToWorld(new Vector2(40.5f, 20.5f));
        sessionSave.SetPlace("known", away.WorldId, saved);

        Assert.That(starts.TryGetStart("known", out WorldDefinition world, out Vector2 position), Is.True);
        Assert.That(world, Is.SameAs(away));
        Assert.That(position, Is.EqualTo(saved));

        Assert.That(starts.TryGetStart("stranger", out world, out position), Is.True);
        Assert.That(world, Is.SameAs(home));
        Assert.That(position, Is.EqualTo(home.ToWorld(new Vector2(10.5f, 12.5f))));
        Assert.That(starts.TryGetStart(string.Empty, out world, out _), Is.True);
        Assert.That(world, Is.SameAs(home));
    }

    /// <summary>
    /// A player saved in a world this build does not list, or at a position on none of its world's chunks, starts at
    /// the starting entry with a warning.
    /// </summary>
    [Test]
    public void APlayerSavedSomewhereGone_StartsAtTheStartingEntry()
    {
        sessionSave.SetPlace("lost", unlisted.WorldId, unlisted.ToWorld(new Vector2(5f, 5f)));
        sessionSave.SetPlace("offChunk", away.WorldId, away.ToWorld(new Vector2(100f, 20f)));
        sessionSave.SetPlace("outside", away.WorldId, away.ToWorld(new Vector2(-3f, 20f)));

        LogAssert.Expect(LogType.Warning, new Regex("does not list"));
        Assert.That(starts.TryGetStart("lost", out WorldDefinition world, out _), Is.True);
        Assert.That(world, Is.SameAs(home));
        LogAssert.Expect(LogType.Warning, new Regex("none of its chunks"));
        Assert.That(starts.TryGetStart("offChunk", out world, out _), Is.True);
        Assert.That(world, Is.SameAs(home));
        LogAssert.Expect(LogType.Warning, new Regex("none of its chunks"));
        Assert.That(starts.TryGetStart("outside", out world, out _), Is.True);
        Assert.That(world, Is.SameAs(home));
    }

    /// <summary>
    /// The saved place is where the player stands on one of its world's chunks; standing off them it has none, so its
    /// previous place is kept.
    /// </summary>
    [Test]
    public void TheSavedPlace_IsWhereThePlayerStands_OnItsWorldsChunks()
    {
        Vector2 standing = away.ToWorld(new Vector2(30.5f, 10.5f));

        Assert.That(GameSaveRecorder.TryGetPlace(away, standing, false, default, out Vector2 place), Is.True);
        Assert.That(place, Is.EqualTo(standing));
        Assert.That(GameSaveRecorder.TryGetPlace(away, home.ToWorld(new Vector2(10.5f, 12.5f)), false, default, out _), Is.False, "Standing in another world.");
        Assert.That(GameSaveRecorder.TryGetPlace(away, away.ToWorld(new Vector2(90f, 10f)), false, default, out _), Is.False, "Standing where the world has no chunk.");
    }

    /// <summary>
    /// A player on a trip into a world is put in it before it moves, so until it reaches the trip's entry its saved
    /// place is that entry; once there, it is where it stands.
    /// </summary>
    [Test]
    public void APlayerOnATrip_IsSavedAtTheTripsEntry_UntilItArrives()
    {
        Vector2 entry = away.ToWorld(new Vector2(32f, 32f));
        StreamInterest arrival = new(away, entry);

        Assert.That(GameSaveRecorder.TryGetPlace(away, home.ToWorld(new Vector2(10.5f, 12.5f)), true, arrival, out Vector2 place), Is.True);
        Assert.That(place, Is.EqualTo(entry), "Still standing in the world it left.");

        Vector2 arrived = entry + new Vector2(1f, 0f);
        Assert.That(GameSaveRecorder.TryGetPlace(away, arrived, true, arrival, out place), Is.True);
        Assert.That(place, Is.EqualTo(arrived), "At the entry, the player is saved where it stands.");
    }

    /// <summary>
    /// A tracked test world named <paramref name="worldName"/> at <paramref name="origin"/> of <paramref name="size"/>
    /// chunks, with scenes only for <paramref name="chunks"/>.
    /// </summary>
    private WorldDefinition MakeWorld(string worldName, Vector2Int origin, Vector2Int size, Vector2Int[] chunks, params WorldDefinition.Entry[] entries)
    {
        WorldDefinition world = WorldDefinition.Create(worldName, origin, size, chunks, entries);
        created.Add(world);
        created.Add(world.RootScene);
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            created.Add(chunk.Scene);
        }

        return world;
    }
}
