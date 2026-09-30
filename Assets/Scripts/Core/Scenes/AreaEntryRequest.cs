public sealed class AreaEntryRequest
{
    public AreaEntryRequest(SceneDefinition area, string spawnId)
    {
        Area = area;
        SpawnId = spawnId;
    }

    public SceneDefinition Area { get; }
    public string SpawnId { get; }
}
