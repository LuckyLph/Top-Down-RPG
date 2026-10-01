using UnityEngine;

public class MobSpawnPoint : MonoBehaviour
{
    [Tooltip("Mob prefab spawned here when the area loads.")]
    [SerializeField] private MobController mobPrefab;

    public MobController MobPrefab => mobPrefab;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.9f);
        Gizmos.DrawWireCube(transform.position, new Vector3(0.6f, 0.6f, 0f));
        Gizmos.DrawLine(transform.position + new Vector3(-0.3f, -0.3f, 0f), transform.position + new Vector3(0.3f, 0.3f, 0f));
    }
}
