using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="TimeFreeze"/>: holding time at zero scale per owner and releasing it.
/// </summary>
public class TimeFreezeTests
{
    private TimeFreeze timeFreeze;

    /// <summary>
    /// Creates a fresh time freeze with no holders.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        timeFreeze = new TimeFreeze();
    }

    /// <summary>
    /// Disposes the time freeze and restores the time scale to 1 so later tests run normally.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        timeFreeze.Dispose();
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Time scale stays at zero while any owner holds the freeze and returns to 1 only after the last owner
    /// releases it.
    /// </summary>
    [Test]
    public void TimeStops_WhileAnyOwnerHolds_AndRunsAfterTheLastRelease()
    {
        object menu = new();
        object transition = new();

        timeFreeze.Hold(menu);
        timeFreeze.Hold(transition);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(timeFreeze.IsFrozen, Is.True);

        timeFreeze.Release(menu);
        Assert.That(Time.timeScale, Is.Zero, "The transition still holds it.");

        timeFreeze.Release(transition);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(timeFreeze.IsFrozen, Is.False);
    }

    /// <summary>
    /// An owner holding twice is released by a single release, and releasing an unknown owner or null leaves
    /// time frozen.
    /// </summary>
    [Test]
    public void HoldingTwice_NeedsOneRelease_AndReleasingAStranger_ChangesNothing()
    {
        object menu = new();
        timeFreeze.Hold(menu);
        timeFreeze.Hold(menu);

        timeFreeze.Release(new object());
        timeFreeze.Release(null);
        Assert.That(Time.timeScale, Is.Zero);

        timeFreeze.Release(menu);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    /// <summary>
    /// Disposing the freeze while it is held restores the time scale and drops every hold.
    /// </summary>
    [Test]
    public void Dispose_RunsTimeAgain()
    {
        timeFreeze.Hold(new object());

        timeFreeze.Dispose();

        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(timeFreeze.IsFrozen, Is.False);
    }
}
