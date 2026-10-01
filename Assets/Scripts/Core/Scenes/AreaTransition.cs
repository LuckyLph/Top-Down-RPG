public readonly struct AreaTransition
{
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
