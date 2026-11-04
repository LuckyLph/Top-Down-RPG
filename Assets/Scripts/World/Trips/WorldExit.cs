using UnityEngine;

/// <summary>
/// What shows an exit in game (Docs/KnownGaps.md, Phase 3), placed by the World Editor's Place markers step: a signpost
/// beside the road for a road's exit to another world, a doorway on its cells for an exit into an interior, a doormat on
/// its cells for an exit out of one, or nothing.
/// </summary>
public enum ExitMarker
{
    Signpost,
    Door,
    Doormat,
    None
}

/// <summary>
/// A way out of a world, placed in one of its chunk scenes: a rectangle of cells that takes the local player who walks
/// into it to <see cref="TargetWorld"/>'s entry <see cref="TargetEntry"/> (its default entry when empty)
/// (Docs/WorldStreaming.md: Travelling between worlds). Its lower-left cell is the one holding its position; it covers
/// <see cref="SizeInCells"/> cells from there, all inside its chunk. Its <see cref="PersistentId"/> names it in trip
/// requests. A marker only: <see cref="WorldExits"/> finds it as its chunk loads, and nothing about it runs per frame.
/// What shows it in game is its <see cref="Marker"/>'s prop, which the World Editor's Place markers step places.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PersistentId))]
public class WorldExit : MonoBehaviour
{
    [Tooltip("The world this exit leads to; it must be listed in Game Scenes.")]
    [SerializeField] private WorldDefinition targetWorld;

    [Tooltip("The name of the target world's entry the player arrives at; empty for its default entry.")]
    [SerializeField] private string targetEntry;

    [Tooltip("How many cells the exit covers across and up, from the cell holding its position.")]
    [SerializeField] private Vector2Int sizeInCells = Vector2Int.one;

    [Tooltip("What shows the exit in game: a signpost beside a road to another world, a doorway into an interior, a doormat out of one, or nothing. Place markers places it.")]
    [SerializeField] private ExitMarker marker = ExitMarker.Signpost;

    private PersistentId persistentId;

    public WorldDefinition TargetWorld => targetWorld;
    public string TargetEntry => targetEntry;
    public Vector2Int SizeInCells => Vector2Int.Max(sizeInCells, Vector2Int.one);
    public ExitMarker Marker => marker;

    /// <summary>
    /// The exit's persistent id; empty when it has none.
    /// </summary>
    public string Id
    {
        get
        {
            if (persistentId == null)
            {
                TryGetComponent(out persistentId);
            }

            return persistentId != null && persistentId.HasId ? persistentId.Id : string.Empty;
        }
    }

    /// <summary>
    /// The cells the exit covers in <paramref name="world"/>, counted from its origin.
    /// </summary>
    public RectInt CellsIn(WorldDefinition world)
    {
        return new RectInt(ChunkCoords.CellOf(world.ToLocal(transform.position)), SizeInCells);
    }

    /// <summary>
    /// Keeps the size at least one cell each way.
    /// </summary>
    private void OnValidate()
    {
        sizeInCells = Vector2Int.Max(sizeInCells, Vector2Int.one);
    }

    /// <summary>
    /// Draws the exit's cells as a cyan box with a cross in the scene view, on the unit cells of the chunk's grid.
    /// </summary>
    private void OnDrawGizmos()
    {
        Vector2 size = SizeInCells;
        Vector3 min = new(Mathf.Floor(transform.position.x), Mathf.Floor(transform.position.y), 0f);
        Vector3 max = min + new Vector3(size.x, size.y, 0f);
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube((min + max) * 0.5f, new Vector3(size.x, size.y, 0f));
        Gizmos.DrawLine(min, max);
        Gizmos.DrawLine(new Vector3(min.x, max.y, 0f), new Vector3(max.x, min.y, 0f));
    }
}
