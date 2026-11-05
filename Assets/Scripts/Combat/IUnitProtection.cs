/// <summary>
/// Which units are out of play for a while: <see cref="HitService"/> ignores hits on them and
/// <see cref="StatusEffectService"/> holds their statuses still. The host keeps a travelling player so until its
/// machine has faded in (<see cref="TripProtection"/>); tests stand fakes in.
/// </summary>
public interface IUnitProtection
{
    /// <summary>
    /// Whether <paramref name="unit"/> is out of play; false for null.
    /// </summary>
    bool IsProtected(DamageReceiver unit);
}
