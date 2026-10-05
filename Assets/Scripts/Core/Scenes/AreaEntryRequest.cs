/// <summary>
/// Which area is being loaded and which spawn point the party enters by. <see cref="GameFlow"/> registers it into the
/// area scene's scope as the scene loads, so the area's <see cref="IAreaEntry"/> knows where to place the players.
/// </summary>
public sealed class AreaEntryRequest
{
    /// <summary>
    /// Creates a request to enter <paramref name="area"/> at the spawn point <paramref name="spawnId"/>.
    /// </summary>
    public AreaEntryRequest(SceneDefinition area, string spawnId)
    {
        Area = area;
        SpawnId = spawnId;
    }

    public SceneDefinition Area { get; }
    public string SpawnId { get; }
}
