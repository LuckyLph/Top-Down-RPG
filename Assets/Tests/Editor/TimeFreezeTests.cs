using NUnit.Framework;
using UnityEngine;

public class TimeFreezeTests
{
    private TimeFreeze timeFreeze;

    [SetUp]
    public void SetUp()
    {
        timeFreeze = new TimeFreeze();
    }

    [TearDown]
    public void TearDown()
    {
        timeFreeze.Dispose();
        Time.timeScale = 1f;
    }

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

    [Test]
    public void Dispose_RunsTimeAgain()
    {
        timeFreeze.Hold(new object());

        timeFreeze.Dispose();

        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(timeFreeze.IsFrozen, Is.False);
    }
}
