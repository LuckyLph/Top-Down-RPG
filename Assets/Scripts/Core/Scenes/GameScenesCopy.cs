/// <summary>
/// A copy of the <see cref="GameScenes"/> asset made at runtime (<see cref="GameScenes.CopyStartingAt"/>), for tests
/// that pin where games start. It has a type of its own so a test can register it in the Main scope beside the asset:
/// VContainer refuses a second singleton of the same concrete type and resolves the later registration of another.
/// </summary>
internal sealed class GameScenesCopy : GameScenes
{
}
