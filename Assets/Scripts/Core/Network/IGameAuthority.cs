/// <summary>
/// Whether this machine decides game state (mob AI, damage, deaths, saves): the host online, or an offline game.
/// Clients only mirror what the host decides.
/// </summary>
public interface IGameAuthority
{
    bool IsAuthoritative { get; }
}
