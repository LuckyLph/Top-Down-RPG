using UnityEngine;

/// <summary>
/// <see cref="IRandom"/> backed by <see cref="System.Random"/>, independent of <see cref="UnityEngine.Random"/>'s
/// global state so a seeded instance gives repeatable sequences.
/// </summary>
public sealed class SystemRandom : IRandom
{
    private readonly System.Random random;

    /// <summary>
    /// Creates a generator with a time-dependent seed.
    /// </summary>
    public SystemRandom()
    {
        random = new System.Random();
    }

    /// <summary>
    /// Creates a generator that always produces the same sequence for <paramref name="seed"/>.
    /// </summary>
    public SystemRandom(int seed)
    {
        random = new System.Random(seed);
    }

    /// <summary>
    /// A uniformly distributed value between <paramref name="minInclusive"/> and <paramref name="maxInclusive"/>,
    /// scaled from <see cref="System.Random.NextDouble"/>.
    /// </summary>
    public float Range(float minInclusive, float maxInclusive)
    {
        return minInclusive + (float)random.NextDouble() * (maxInclusive - minInclusive);
    }

    /// <summary>
    /// A point uniformly distributed over the unit disc's area: a random angle with the square root of a random
    /// radius.
    /// </summary>
    public Vector2 InsideUnitCircle()
    {
        float angle = (float)random.NextDouble() * 2f * Mathf.PI;
        float radius = Mathf.Sqrt((float)random.NextDouble());
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
}
