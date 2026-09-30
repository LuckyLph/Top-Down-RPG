public sealed class UnityClock : IClock
{
    public static readonly UnityClock Shared = new();

    public float Time => UnityEngine.Time.time;
}
