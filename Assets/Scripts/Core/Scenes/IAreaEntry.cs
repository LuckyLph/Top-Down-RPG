/// <summary>
/// What an area scene's scope provides so <see cref="GameFlow"/> can bring the party in once the scene is loaded and
/// active.
/// </summary>
public interface IAreaEntry
{
    /// <summary>
    /// Makes this area the active one and places the players at the requested spawn point.
    /// </summary>
    void Enter();
}
