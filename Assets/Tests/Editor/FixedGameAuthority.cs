public sealed class FixedGameAuthority : IGameAuthority
{
    public static readonly FixedGameAuthority Authoritative = new(true);

    public FixedGameAuthority(bool isAuthoritative)
    {
        IsAuthoritative = isAuthoritative;
    }

    public bool IsAuthoritative { get; }
}
