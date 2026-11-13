using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

/// <summary>
/// Tests for the chat's log on one machine (Docs/UiRevamp.md, Phase 12): lines kept oldest first, each stamped with the
/// game time and the local time of day it arrived, the oldest dropping off past its capacity, players' lines only from
/// a slot, empty lines ignored, and emptying it.
/// </summary>
public class ChatLogTests
{
    private ManualClock clock;
    private ManualWallClock wallClock;
    private ChatLog log;
    private List<ChatLine> added;

    /// <summary>
    /// An empty log on a manual game clock and wall clock, recording what it raises.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        wallClock = new ManualWallClock();
        log = new ChatLog(clock, wallClock);
        added = new List<ChatLine>();
        log.Added += added.Add;
    }

    /// <summary>
    /// A player's line and a party line are kept oldest first, the player's naming its slot and the party's none, each
    /// with the game time it arrived and the local time of day, "14:32", and each raised as it arrives.
    /// </summary>
    [Test]
    public void Lines_AreKeptOldestFirst_StampedWithTheGameTimeAndTheTimeOfDay()
    {
        clock.Time = 5f;
        log.AddPlayerLine(2, "on my way");
        clock.Time = 7f;
        wallClock.Advance(120);
        log.AddPartyLine("Player 3 joined");

        Assert.That(log.Count, Is.EqualTo(2));
        Assert.That(log[0].Slot, Is.EqualTo(2));
        Assert.That(log[0].IsParty, Is.False);
        Assert.That(log[0].Text, Is.EqualTo("on my way"));
        Assert.That(log[0].Time, Is.EqualTo(5f));
        Assert.That(log[0].Stamp, Is.EqualTo(wallClock.UtcNow.AddSeconds(-120).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)));
        Assert.That(log[1].IsParty, Is.True);
        Assert.That(log[1].Time, Is.EqualTo(7f));
        Assert.That(log[1].Stamp, Is.EqualTo(wallClock.UtcNow.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)));
        Assert.That(added.Count, Is.EqualTo(2));
        Assert.That(added[1].Text, Is.EqualTo("Player 3 joined"));
    }

    /// <summary>
    /// Past its capacity the oldest lines drop off, the rest keeping their order.
    /// </summary>
    [Test]
    public void PastItsCapacity_TheOldestLinesDropOff()
    {
        for (int i = 0; i < ChatLog.Capacity + 5; i++)
        {
            log.AddPlayerLine(1, i.ToString(CultureInfo.InvariantCulture));
        }

        Assert.That(log.Count, Is.EqualTo(ChatLog.Capacity));
        Assert.That(log[0].Text, Is.EqualTo("5"));
        Assert.That(log[ChatLog.Capacity - 1].Text, Is.EqualTo((ChatLog.Capacity + 4).ToString(CultureInfo.InvariantCulture)));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = log[ChatLog.Capacity]);
    }

    /// <summary>
    /// A player's line from no slot and an empty line are ignored; emptying the log forgets every line and says so.
    /// </summary>
    [Test]
    public void LinesWithoutASlotOrText_AreIgnored_AndClearingForgetsEveryLine()
    {
        log.AddPlayerLine(0, "nobody");
        log.AddPlayerLine(5, "nobody either");
        log.AddPlayerLine(1, string.Empty);
        log.AddPartyLine(null);
        Assert.That(log.Count, Is.Zero);
        Assert.That(added, Is.Empty);

        log.AddPlayerLine(4, "hello");
        bool cleared = false;
        log.Cleared += () => cleared = true;
        log.Clear();
        Assert.That(log.Count, Is.Zero);
        Assert.That(cleared, Is.True);
    }
}
