using UnityEngine;

/// <summary>
/// How far around each player chunks load and unload, and how long a world nobody is in stays loaded
/// (Docs/WorldStreaming.md: Load rule, Empty worlds).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/World Streaming Settings", fileName = "WorldStreamingSettings")]
public class WorldStreamingSettings : ScriptableObject
{
    [Tooltip("Cells a player's view covers, across and up; chunks load around this rectangle, not around a radius.")]
    [SerializeField] private Vector2Int viewCells = new(20, 12);

    [Tooltip("Cells beyond the view on every side within which every chunk is loaded.")]
    [SerializeField, Min(0)] private int loadMargin = 16;

    [Tooltip("Cells beyond the load margin a chunk must lie from every player before it unloads, so walking back and forth over a border reloads nothing.")]
    [SerializeField, Min(0)] private int unloadHysteresis = 8;

    [Tooltip("Seconds of game time a world nobody is in keeps itself and its chunks before unloading.")]
    [SerializeField, Min(0f)] private float emptyWorldGraceSeconds = 10f;

    public Vector2Int ViewCells => viewCells;
    public int LoadMargin => loadMargin;
    public int UnloadHysteresis => unloadHysteresis;
    public float EmptyWorldGraceSeconds => emptyWorldGraceSeconds;

    /// <summary>
    /// The cells around a player's cell on each side of its interest rectangle: the view plus the load margin.
    /// </summary>
    public Vector2Int LoadExtents => ChunkStreamer.ExtentsOf(viewCells, loadMargin);

    /// <summary>
    /// The cells around a player's cell on each side of the rectangle a loaded chunk must leave to unload: the view
    /// plus the load margin and the unload hysteresis.
    /// </summary>
    public Vector2Int KeepExtents => ChunkStreamer.ExtentsOf(viewCells, loadMargin + unloadHysteresis);

    /// <summary>
    /// Whether a mob of <paramref name="config"/> always has its home chunk loaded while it is near a player: its leash
    /// plus its detection radius stays under the load extents, so any player it can detect, wherever its leash lets it
    /// be, holds its home chunk in its interest rectangle; and its leash plus its lose distance stays under the keep
    /// extents, so that chunk never unloads under it while it chases (Docs/WorldStreaming.md: Leash). The smaller side of
    /// each rectangle counts, since a mob may stand above or beside its player. False, with the reason in
    /// <paramref name="problem"/>, otherwise.
    /// </summary>
    public bool KeepsHomeChunkLoaded(MobConfig config, out string problem)
    {
        int loadReach = Mathf.Min(LoadExtents.x, LoadExtents.y);
        int keepReach = Mathf.Min(KeepExtents.x, KeepExtents.y);
        if (config.LeashCells + config.DetectionRadius >= loadReach)
        {
            problem = $"{config.name}: leash {config.LeashCells} + detection radius {config.DetectionRadius} must stay under {loadReach} cells, the load extents' smaller side.";
            return false;
        }

        if (config.LeashCells + config.LoseTargetDistance >= keepReach)
        {
            problem = $"{config.name}: leash {config.LeashCells} + lose distance {config.LoseTargetDistance} must stay under {keepReach} cells, the keep extents' smaller side.";
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// A streamer for these settings that keeps the chunks <paramref name="holds"/> says another machine holds.
    /// </summary>
    public ChunkStreamer CreateStreamer(IChunkHolds holds = null)
    {
        return new ChunkStreamer(viewCells, loadMargin, unloadHysteresis, emptyWorldGraceSeconds, holds);
    }

    /// <summary>
    /// Keeps the view at least one cell across.
    /// </summary>
    private void OnValidate()
    {
        viewCells = Vector2Int.Max(viewCells, Vector2Int.one);
    }
}
