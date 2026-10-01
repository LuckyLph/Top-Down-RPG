using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private string spawnId = "start";
    [Tooltip("Horizontal gap between party members placed at this spawn point.")]
    [SerializeField, Min(0f)] private float partySpacing = 0.6f;

    public string SpawnId => spawnId;

    public Vector2 GetSlotPosition(int slotIndex)
    {
        int step = (slotIndex + 1) / 2;
        float side = slotIndex % 2 == 1 ? 1f : -1f;
        return (Vector2)transform.position + Vector2.right * (step * side * partySpacing);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.35f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * 0.5f);
    }
}
