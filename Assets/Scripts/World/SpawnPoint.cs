using UnityEngine;

/// <summary>
/// A named place in an area where the party arrives, chosen by <see cref="SpawnId"/> when the area is entered, and
/// where its players respawn. Party members stand side by side around it.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private string spawnId = "start";
    [Tooltip("Horizontal gap between party members placed at this spawn point.")]
    [SerializeField, Min(0f)] private float partySpacing = 0.6f;

    public string SpawnId => spawnId;

    /// <summary>
    /// World position for party member <paramref name="slotIndex"/>: slot 0 on the point, then alternating right
    /// and left, one party spacing further out every two slots.
    /// </summary>
    public Vector2 GetSlotPosition(int slotIndex)
    {
        int step = (slotIndex + 1) / 2;
        float side = slotIndex % 2 == 1 ? 1f : -1f;
        return (Vector2)transform.position + Vector2.right * (step * side * partySpacing);
    }

    /// <summary>
    /// Draws a cyan circle with a short downward tick at the spawn position in the scene view.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.35f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * 0.5f);
    }
}
