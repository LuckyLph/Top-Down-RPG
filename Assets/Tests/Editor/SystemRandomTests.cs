using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the seeded <see cref="SystemRandom"/>: value bounds and determinism per seed.
/// </summary>
public class SystemRandomTests
{
    /// <summary>
    /// <see cref="SystemRandom.Range"/> always returns a value between its minimum and maximum.
    /// </summary>
    [Test]
    public void Range_StaysWithinBounds()
    {
        SystemRandom random = new(42);

        for (int i = 0; i < 1000; i++)
        {
            float value = random.Range(-2f, 3f);
            Assert.That(value, Is.InRange(-2f, 3f));
        }
    }

    /// <summary>
    /// <see cref="SystemRandom.InsideUnitCircle"/> never returns a point farther than 1 from the origin.
    /// </summary>
    [Test]
    public void InsideUnitCircle_NeverLeavesTheUnitCircle()
    {
        SystemRandom random = new(42);

        for (int i = 0; i < 1000; i++)
        {
            Assert.That(random.InsideUnitCircle().magnitude, Is.LessThanOrEqualTo(1f));
        }
    }

    /// <summary>
    /// Two generators with the same seed produce the same sequence of ranges and circle points.
    /// </summary>
    [Test]
    public void SameSeed_ProducesTheSameSequence()
    {
        SystemRandom first = new(7);
        SystemRandom second = new(7);

        for (int i = 0; i < 10; i++)
        {
            Assert.That(first.Range(0f, 1f), Is.EqualTo(second.Range(0f, 1f)));
            Assert.That(first.InsideUnitCircle(), Is.EqualTo(second.InsideUnitCircle()));
        }
    }
}
