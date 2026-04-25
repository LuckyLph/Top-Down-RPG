using UnityEngine;

public class MobPatrolAnchor : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawPatrolGizmos = true;
    [SerializeField] private Color patrolRadiusColor = new(0.6f, 0.4f, 1f, 0.75f);
    [SerializeField] private Color spawnPointColor = new(0.9f, 0.9f, 1f, 0.85f);

    private NavigationGrid2D navigationGrid;
    private MobConfig config;
    private Vector2 spawnPosition;

    public Vector2 SpawnPosition => spawnPosition;

    private void Awake()
    {
        spawnPosition = transform.position;
    }

    public void Initialize(NavigationGrid2D navGrid, MobConfig mobConfig)
    {
        navigationGrid = navGrid;
        config = mobConfig;
        spawnPosition = transform.position;
    }

    public float GetIdleDuration()
    {
        return config != null ? config.NextIdleDuration() : Random.Range(0.5f, 1.5f);
    }

    public bool TryGetRoamDestination(out Vector2 destination)
    {
        destination = spawnPosition;

        if (navigationGrid == null || config == null)
        {
            return false;
        }

        int attempts = Mathf.Max(1, config.patrolSampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * config.patrolRoamRadius;
            Vector2 candidateWorld = spawnPosition + offset;
            Vector3Int candidateCell = navigationGrid.WorldToCell(candidateWorld);

            if (navigationGrid.IsCellWalkable(candidateCell, config.MovementProfile))
            {
                destination = navigationGrid.CellToWorldCenter(candidateCell);
                return true;
            }

            if (navigationGrid.TryGetNearestWalkableCell(candidateCell, config.MovementProfile, out Vector3Int nearest, config.nearestCellSearchRadius))
            {
                destination = navigationGrid.CellToWorldCenter(nearest);
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPatrolGizmos)
        {
            return;
        }

        Vector3 center = spawnPosition;
        if (!Application.isPlaying && center == Vector3.zero)
        {
            center = transform.position;
        }

        Gizmos.color = patrolRadiusColor;
        float radius = config != null ? config.patrolRoamRadius : 0f;
        if (radius > 0f)
        {
            Gizmos.DrawWireSphere(center, radius);
        }

        Gizmos.color = spawnPointColor;
        Gizmos.DrawWireSphere(center, 0.08f);
    }
}
