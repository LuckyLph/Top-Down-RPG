/// <summary>
/// Test clock that only moves when told to.
/// </summary>
public sealed class ManualClock : IClock
{
    public float Time { get; set; }

    /// <summary>
    /// Moves <see cref="Time"/> forward by <paramref name="seconds"/>.
    /// </summary>
    public void Advance(float seconds)
    {
        Time += seconds;
    }
}
