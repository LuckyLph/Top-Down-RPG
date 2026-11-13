using System;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="ContinueLine"/> (Docs/UiRevamp.md, Phase 9): the time played in hours and minutes, when the
/// save was made (today, yesterday, a date this year, a date with its year), the place as the display name of the entry
/// nearest the local player, and the whole line read from the latest save.
/// </summary>
public class ContinueLineTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 18, 30, 0, DateTimeKind.Local);

    /// <summary>
    /// The time played reads in whole hours and minutes, and under a minute as such.
    /// </summary>
    [TestCase(20d, "under a minute")]
    [TestCase(60d, "1 min")]
    [TestCase(59d * 60d + 59d, "59 min")]
    [TestCase(3600d, "1 h")]
    [TestCase(3d * 3600d + 12d * 60d + 40d, "3 h 12 min")]
    [TestCase(125d * 3600d + 59d * 60d, "125 h 59 min")]
    public void TheTimePlayed_ReadsInHoursAndMinutes(double seconds, string shown)
    {
        Assert.That(ContinueLine.Played(seconds), Is.EqualTo(shown));
    }

    /// <summary>
    /// When the save was made reads as today's or yesterday's time, a date this year with its time, or a date with its
    /// year before it, on a 24-hour clock.
    /// </summary>
    [Test]
    public void WhenItWasSaved_ReadsAsTodayYesterdayOrADate()
    {
        Assert.That(ContinueLine.Saved(new DateTime(2026, 11, 12, 14, 2, 0), Now), Is.EqualTo("today 14:02"));
        Assert.That(ContinueLine.Saved(new DateTime(2026, 11, 11, 23, 59, 0), Now), Is.EqualTo("yesterday 23:59"));
        Assert.That(ContinueLine.Saved(new DateTime(2026, 11, 10, 9, 5, 0), Now), Is.EqualTo("10 Nov 09:05"));
        Assert.That(ContinueLine.Saved(new DateTime(2025, 3, 2, 9, 5, 0), Now), Is.EqualTo("2 Mar 2025"));
        Assert.That(ContinueLine.Format("Clearing", 3d * 3600d + 12d * 60d, new DateTime(2026, 11, 12, 14, 2, 0), Now), Is.EqualTo("Clearing · 3 h 12 min · today 14:02"));
        Assert.That(ContinueLine.Format(string.Empty, 90d, new DateTime(2026, 11, 12, 14, 2, 0), Now), Is.EqualTo("1 min · today 14:02"), "A save in a world this build does not list leaves the place out.");
    }

    /// <summary>
    /// The place is the entry nearest the local player's saved place, else the save's first player's; a world this
    /// build does not list has none; and the line reads from the latest save, or there is none.
    /// </summary>
    [Test]
    public void ThePlace_IsTheNearestEntry_AndTheLineReadsTheLatestSave()
    {
        using MenuTestRig rig = new();
        GameSaveData data = new(0d, new[]
        {
            new GameSaveData.Player("someone-else", rig.Home.WorldId, rig.Home.ToWorld(new Vector2(11f, 12f))),
            new GameSaveData.Player(rig.Settings.PlayerId, rig.Home.WorldId, rig.Home.ToWorld(MenuTestRig.NearNorth)),
        }, new GameSaveData.World[0]);

        Assert.That(ContinueLine.PlaceOf(data, rig.Settings.PlayerId, rig.Scenes), Is.EqualTo("North road"));
        Assert.That(ContinueLine.PlaceOf(data, "nobody", rig.Scenes), Is.EqualTo("Start camp"), "The first player's place when the local one is not in it.");
        GameSaveData elsewhere = new(0d, new[] { new GameSaveData.Player(rig.Settings.PlayerId, "unlisted-world", Vector2.zero) }, new GameSaveData.World[0]);
        Assert.That(ContinueLine.PlaceOf(elsewhere, rig.Settings.PlayerId, rig.Scenes), Is.Empty);

        Assert.That(rig.Line.TryGet(out _), Is.False, "No save, no line.");
        GameSaveData saved = new(2d * 3600d, new[] { new GameSaveData.Player(rig.Settings.PlayerId, rig.Home.WorldId, rig.Home.ToWorld(new Vector2(10f, 12f))) }, new GameSaveData.World[0]);
        saved.SetSession("session");
        rig.Save.Write(saved);
        Assert.That(rig.Line.TryGet(out string line), Is.True);
        Assert.That(line, Does.StartWith("Start camp · 2 h · "));
    }
}
