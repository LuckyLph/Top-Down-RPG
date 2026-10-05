/// <summary>
/// <see cref="IGameAuthority"/> that is always or never authoritative, for testing host and client behaviour.
/// </summary>
public sealed class FixedGameAuthority : IGameAuthority
{
    public static readonly FixedGameAuthority Authoritative = new(true);

    /// <summary>
    /// Creates an authority that answers <paramref name="isAuthoritative"/> for its whole lifetime.
    /// </summary>
    public FixedGameAuthority(bool isAuthoritative)
    {
        IsAuthoritative = isAuthoritative;
    }

    public bool IsAuthoritative { get; }
}
