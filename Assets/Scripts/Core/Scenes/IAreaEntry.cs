// Registered by an area scene's LifetimeScope. GameFlow calls it right after the area loads
// so the area can place the player at the requested spawn point.
public interface IAreaEntry
{
    void Enter(string spawnId);
}
