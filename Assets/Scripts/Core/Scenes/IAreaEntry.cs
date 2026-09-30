// Registered by an area scene's LifetimeScope, which also receives the AreaEntryRequest GameFlow
// enqueued for that load. GameFlow calls Enter synchronously right after the area becomes the
// active scene (not from an entry point, which would run on a later player-loop phase), so the
// player is placed before the screen fades back in.
public interface IAreaEntry
{
    void Enter();
}
