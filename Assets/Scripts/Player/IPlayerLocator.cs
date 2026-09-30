using UnityEngine;

// Read-only handle on the session's player for systems in other scenes (mobs, HUD, death handling).
public interface IPlayerLocator
{
    Transform Transform { get; }
    Health Health { get; }
    bool IsAlive { get; }
}
