using UnityEngine;

public sealed class PlayerLocator : IPlayerLocator
{
    public PlayerLocator(Transform transform, Health health)
    {
        Transform = transform;
        Health = health;
    }

    public Transform Transform { get; }
    public Health Health { get; }
    public bool IsAlive => Transform != null && (Health == null || !Health.IsDead);
}
