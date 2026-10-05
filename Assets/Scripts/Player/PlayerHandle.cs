using UnityEngine;

/// <summary>
/// A player's identity in the <see cref="IPlayerRegistry"/>, local or remote: its transform and health, compared by
/// reference.
/// </summary>
public sealed class PlayerHandle
{
    /// <summary>
    /// Creates a handle over a player's <paramref name="transform"/> and <paramref name="health"/> (null health
    /// counts as always alive while the transform exists).
    /// </summary>
    public PlayerHandle(Transform transform, Health health)
    {
        Transform = transform;
        Health = health;
    }

    public Transform Transform { get; }
    public Health Health { get; }
    public bool IsAlive => Transform != null && (Health == null || !Health.IsDead);
}
