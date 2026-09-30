// Scaled game time from UnityEngine.Time. Stateless, so one shared instance serves every consumer,
// including components that were never injected.
public sealed class UnityClock : IClock
{
    public static readonly UnityClock Shared = new();

    public float Time => UnityEngine.Time.time;
}
