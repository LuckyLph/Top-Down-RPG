using System.Collections.Generic;

/// <summary>
/// An <see cref="IUnitProtection"/> the test fills directly: the units in <see cref="Protected"/> are out of play.
/// </summary>
internal sealed class FakeUnitProtection : IUnitProtection
{
    public HashSet<DamageReceiver> Protected { get; } = new();

    /// <summary>
    /// Whether <paramref name="unit"/> is in <see cref="Protected"/>.
    /// </summary>
    public bool IsProtected(DamageReceiver unit)
    {
        return unit != null && Protected.Contains(unit);
    }
}
