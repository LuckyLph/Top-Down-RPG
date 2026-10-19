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
    /// A streamer for these settings.
    /// </summary>
    public ChunkStreamer CreateStreamer()
    {
        return new ChunkStreamer(viewCells, loadMargin, unloadHysteresis, emptyWorldGraceSeconds);
    }

    /// <summary>
    /// Keeps the view at least one cell across.
    /// </summary>
    private void OnValidate()
    {
        viewCells = Vector2Int.Max(viewCells, Vector2Int.one);
    }
}
