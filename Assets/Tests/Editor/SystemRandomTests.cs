using NUnit.Framework;
using UnityEngine;

public class SystemRandomTests
{
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

    [Test]
    public void InsideUnitCircle_NeverLeavesTheUnitCircle()
    {
        SystemRandom random = new(42);

        for (int i = 0; i < 1000; i++)
        {
            Assert.That(random.InsideUnitCircle().magnitude, Is.LessThanOrEqualTo(1f));
        }
    }

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
