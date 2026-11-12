using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the chat's lines about the party (Docs/UiRevamp.md, Phase 12), raised on one machine from what it knows,
/// over players whose slots are set without the network: "Player 2 joined" when a player's joining ends, but not for
/// one found already joined; "Player 2 is down" when it dies; "Player 2 left" when it leaves, with the slot it had; and
/// none of them about the local player or a player without a slot.
/// </summary>
public class ChatPartyLinesTests
{
    private readonly List<Object> created = new();
    private PlayerRegistry players;
    private LocalPlayerTracker localPlayer;
    private ChatLog log;
    private ChatPartyLines lines;

    /// <summary>
    /// Starts the party lines over an empty registry and log, with a local player in slot 1.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        players = new PlayerRegistry();
        localPlayer = new LocalPlayerTracker();
        log = new ChatLog(new ManualClock(), new ManualWallClock());
        lines = new ChatPartyLines(players, localPlayer, log);
        lines.Start();
        AddPlayer("Local", 1, joining: false, isLocal: true);
    }

    /// <summary>
    /// Disposes the party lines and destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        lines?.Dispose();
        lines = null;
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A player that arrives joining says nothing until its joining ends, then "Player 2 joined", once; a player found
    /// already joined, as a late joiner finds the others, says nothing.
    /// </summary>
    [Test]
    public void APlayersJoiningEnding_SaysItJoined_ButOneFoundJoinedSaysNothing()
    {
        (PlayerSlot slot, _, _) = AddPlayer("Joiner", 2, joining: true);
        Assert.That(Texts(), Is.Empty);

        slot.Configure(2, false);
        slot.Configure(2, false);
        Assert.That(Texts(), Is.EqualTo(new[] { "Player 2 joined" }));

        AddPlayer("Already there", 3, joining: false);
        Assert.That(Texts(), Is.EqualTo(new[] { "Player 2 joined" }), "Found joined, it says nothing.");
    }

    /// <summary>
    /// A player that dies here says "Player 3 is down"; one that leaves says "Player 3 left", with the slot it had even
    /// when its slot reads none as it goes, and nothing more once gone.
    /// </summary>
    [Test]
    public void APlayerGoingDown_AndLeaving_SaysSo()
    {
        (PlayerSlot slot, Health health, PlayerHandle handle) = AddPlayer("Other", 3, joining: false);
        health.ApplyDamage(health.MaxHealth);
        Assert.That(Texts(), Is.EqualTo(new[] { "Player 3 is down" }));

        slot.Configure(0, false);
        players.Remove(handle);
        Assert.That(Texts(), Is.EqualTo(new[] { "Player 3 is down", "Player 3 left" }));

        health.Restore();
        health.ApplyDamage(health.MaxHealth);
        Assert.That(log.Count, Is.EqualTo(2), "Gone, it is followed no more.");
    }

    /// <summary>
    /// The local player's own joining, death and leaving say nothing, and neither does a player without a slot, as every
    /// player is offline.
    /// </summary>
    [Test]
    public void TheLocalPlayer_AndAPlayerWithoutASlot_SayNothing()
    {
        PlayerHandle local = players.Players[0];
        local.Health.ApplyDamage(local.Health.MaxHealth);
        (PlayerSlot slot, Health health, PlayerHandle handle) = AddPlayer("Offline", 0, joining: true);
        slot.Configure(0, false);
        health.ApplyDamage(health.MaxHealth);
        players.Remove(handle);
        players.Remove(local);

        Assert.That(Texts(), Is.Empty);
    }

    /// <summary>
    /// The texts of the log's lines, oldest first.
    /// </summary>
    private string[] Texts()
    {
        string[] texts = new string[log.Count];
        for (int i = 0; i < log.Count; i++)
        {
            Assert.That(log[i].IsParty, Is.True);
            texts[i] = log[i].Text;
        }

        return texts;
    }

    /// <summary>
    /// Registers a tracked player in <paramref name="slot"/>, joining or not, with 100 HP, made the local one when
    /// <paramref name="isLocal"/>; returns its slot, health and handle.
    /// </summary>
    private (PlayerSlot, Health, PlayerHandle) AddPlayer(string name, int slot, bool joining, bool isLocal = false)
    {
        GameObject unit = new(name);
        created.Add(unit);
        PlayerController controller = unit.AddComponent<PlayerController>();
        Health health = unit.AddComponent<Health>();
        SerializedObject healthSettings = new(health);
        healthSettings.FindProperty("maxHealth").intValue = 100;
        healthSettings.ApplyModifiedPropertiesWithoutUndo();
        PlayerSlot playerSlot = unit.AddComponent<PlayerSlot>();
        playerSlot.Configure(slot, joining);
        LocalPlayer asLocal = isLocal ? new LocalPlayer(controller) : null;
        PlayerHandle handle = asLocal != null ? asLocal.Handle : new PlayerHandle(unit.transform, health);
        players.Add(handle);
        if (asLocal != null)
        {
            localPlayer.Assign(asLocal);
        }

        return (playerSlot, health, handle);
    }
}
