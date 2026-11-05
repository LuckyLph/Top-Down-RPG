/// <summary>
/// The engine-backed <see cref="IClock"/>: reads the scaled <see cref="UnityEngine.Time.time"/>, so it stops while
/// <see cref="TimeFreeze"/> holds time still. The Main scope registers one for the whole app.
/// </summary>
public sealed class UnityClock : IClock
{
    public float Time => UnityEngine.Time.time;
}
