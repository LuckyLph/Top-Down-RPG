// Game time source for cooldowns, so gameplay code can be driven by a manual clock in tests.
public interface IClock
{
    float Time { get; }
}
