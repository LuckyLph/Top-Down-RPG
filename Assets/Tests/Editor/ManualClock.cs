// Test clock that only moves when told to.
public sealed class ManualClock : IClock
{
    public float Time { get; set; }

    public void Advance(float seconds)
    {
        Time += seconds;
    }
}
