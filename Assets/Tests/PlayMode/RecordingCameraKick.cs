using UnityEngine;

/// <summary>
/// <see cref="ICameraKick"/> that only records the kicks it gets, standing in for an in-process client's camera.
/// </summary>
public sealed class RecordingCameraKick : ICameraKick
{
    public int Count { get; private set; }
    public Vector2 LastOffset { get; private set; }

    /// <summary>
    /// Records the kick.
    /// </summary>
    public void Kick(Vector2 offset, float seconds)
    {
        Count++;
        LastOffset = offset;
    }
}
