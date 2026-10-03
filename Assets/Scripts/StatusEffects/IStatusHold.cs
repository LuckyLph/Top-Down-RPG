/// <summary>
/// Says whether a unit's statuses are held still: neither ticking nor running down. The host holds a client's
/// player while that client is still loading the current area.
/// </summary>
public interface IStatusHold
{
    bool IsHeld { get; }
}
