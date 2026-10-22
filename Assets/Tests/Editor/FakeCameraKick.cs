using UnityEngine;

/// <summary>
/// <see cref="ICameraKick"/> that records the kicks it gets.
/// </summary>
public sealed class FakeCameraKick : ICameraKick
{
    public int Count { get; private set; }
    public Vector2 LastOffset { get; private set; }
    public float LastSeconds { get; private set; }

    /// <summary>
    /// Records the kick.
    /// </summary>
    public void Kick(Vector2 offset, float seconds)
    {
        Count++;
        LastOffset = offset;
        LastSeconds = seconds;
    }
}
