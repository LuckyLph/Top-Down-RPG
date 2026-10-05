/// <summary>
/// The engine-backed <see cref="IClock"/>: reads the scaled <see cref="UnityEngine.Time.time"/>, so it stops while
/// <see cref="TimeFreeze"/> holds time still. One shared stateless instance serves the whole app.
/// </summary>
public sealed class UnityClock : IClock
{
    public static readonly UnityClock Shared = new();

    public float Time => UnityEngine.Time.time;
}
