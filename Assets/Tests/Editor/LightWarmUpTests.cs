using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The light warm-up (Docs/LightingGaps.md, Phase 5): it draws its casting light and caster once, on its second tick,
/// once both have built their meshes, and destroys everything the tick after, leaving nothing behind; disposed early,
/// it leaves nothing either.
/// </summary>
public class LightWarmUpTests
{
    private const string RootName = "Light Warm-Up";

    /// <summary>
    /// Started and ticked three times, the warm-up draws on the second tick and is gone after the third.
    /// </summary>
    [Test]
    public void TheWarmUp_DrawsOnce_ThenLeavesNothing()
    {
        LightWarmUp warmUp = new();
        try
        {
            warmUp.Start();
            Assert.That(warmUp.Running, Is.True);
            Assert.That(Leftovers(), Is.EqualTo(1));

            warmUp.Tick();
            Assert.That(warmUp.Rendered, Is.False, "Not before the light and caster have had a frame.");
            warmUp.Tick();
            Assert.That(warmUp.Rendered, Is.True);
            Assert.That(warmUp.Running, Is.True);
            warmUp.Tick();
            Assert.That(warmUp.Running, Is.False);
            Assert.That(Leftovers(), Is.Zero);
        }
        finally
        {
            warmUp.Dispose();
        }
    }

    /// <summary>
    /// Disposed before it draws, the warm-up leaves nothing behind.
    /// </summary>
    [Test]
    public void DisposedEarly_TheWarmUp_LeavesNothing()
    {
        LightWarmUp warmUp = new();
        warmUp.Start();
        warmUp.Dispose();

        Assert.That(warmUp.Running, Is.False);
        Assert.That(warmUp.Rendered, Is.False);
        Assert.That(Leftovers(), Is.Zero);
    }

    /// <summary>
    /// How many of the warm-up's roots exist.
    /// </summary>
    private static int Leftovers()
    {
        int count = 0;
        foreach (GameObject made in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            count += made.name == RootName ? 1 : 0;
        }

        return count;
    }
}
