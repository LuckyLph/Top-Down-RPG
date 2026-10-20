using UnityEngine;

/// <summary>
/// Marks where one mob of the given prefab spawns in a chunk scene. <see cref="ChunkMobSpawner"/> spawns them when the
/// chunk loads; mobs are never placed in scenes directly.
/// </summary>
public class MobSpawnPoint : MonoBehaviour
{
    [Tooltip("Mob prefab spawned here when the chunk loads.")]
    [SerializeField] private MobController mobPrefab;

    public MobController MobPrefab => mobPrefab;

    /// <summary>
    /// Draws a red crossed square at the spawn position in the scene view.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.9f);
        Gizmos.DrawWireCube(transform.position, new Vector3(0.6f, 0.6f, 0f));
        Gizmos.DrawLine(transform.position + new Vector3(-0.3f, -0.3f, 0f), transform.position + new Vector3(0.3f, 0.3f, 0f));
    }
}
