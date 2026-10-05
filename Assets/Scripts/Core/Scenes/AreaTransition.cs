/// <summary>
/// An area load <see cref="GameFlow"/> is about to run, raised through <see cref="GameFlow.AreaLoading"/>: the target
/// area, the spawn point, and whether it starts a new game session (reloading the gameplay scene) or only changes area.
/// </summary>
public readonly struct AreaTransition
{
    /// <summary>
    /// Creates a transition into <paramref name="area"/> at <paramref name="spawnId"/>; <paramref name="newSession"/>
    /// marks a new game rather than an area change.
    /// </summary>
    public AreaTransition(SceneDefinition area, string spawnId, bool newSession)
    {
        Area = area;
        SpawnId = spawnId;
        NewSession = newSession;
    }

    public SceneDefinition Area { get; }
    public string SpawnId { get; }
    public bool NewSession { get; }
}
