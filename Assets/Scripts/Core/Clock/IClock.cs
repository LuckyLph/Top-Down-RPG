/// <summary>
/// Game time source for cooldowns, so gameplay code can be driven by a manual clock in tests.
/// </summary>
public interface IClock
{
    float Time { get; }
}
