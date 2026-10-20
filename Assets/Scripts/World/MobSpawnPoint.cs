using UnityEngine;

/// <summary>
/// Marks where one mob of the given prefab spawns in an area or chunk scene. <see cref="AreaMobSpawner"/> spawns them
/// when the area loads and <see cref="ChunkMobSpawner"/> when the chunk does; mobs are never placed in scenes directly.
/// </summary>
public class MobSpawnPoint : MonoBehaviour
{
    [Tooltip("Mob prefab spawned here when the area or chunk loads.")]
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
