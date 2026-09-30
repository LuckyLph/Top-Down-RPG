using UnityEngine;

public interface IPlayerLocator
{
    Transform Transform { get; }
    Health Health { get; }
    bool IsAlive { get; }
}
