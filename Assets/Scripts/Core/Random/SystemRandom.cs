using UnityEngine;

public sealed class SystemRandom : IRandom
{
    private readonly System.Random random;

    public SystemRandom()
    {
        random = new System.Random();
    }

    public SystemRandom(int seed)
    {
        random = new System.Random(seed);
    }

    public float Range(float minInclusive, float maxInclusive)
    {
        return minInclusive + (float)random.NextDouble() * (maxInclusive - minInclusive);
    }

    public Vector2 InsideUnitCircle()
    {
        float angle = (float)random.NextDouble() * 2f * Mathf.PI;
        float radius = Mathf.Sqrt((float)random.NextDouble());
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
}
