// Where the player should enter an area. GameFlow registers it into the area's container while
// the area scene loads, so the area's IAreaEntry receives it by constructor.
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
