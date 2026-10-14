using UnityEngine;

/// <summary>
/// A named place in an area where the party arrives, chosen by <see cref="SpawnId"/> when the area is entered, and
/// where its players respawn. Party members stand side by side around it.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    private const int GizmoSegments = 24;
    private const float GizmoRadius = 0.35f;

    private static readonly Vector3[] GizmoCircle = FlatCircle(GizmoSegments, GizmoRadius);

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
    /// Draws a cyan circle with a short downward tick at the spawn position in the scene view, flat in the ground's
    /// plane (a wire sphere stood out of the ground in a tilted view).
    /// </summary>
    private void OnDrawGizmos()
    {
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.Translate(transform.position);
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawLineStrip(GizmoCircle, true);
        Gizmos.DrawLine(Vector3.zero, Vector3.down * 0.5f);
        Gizmos.matrix = previous;
    }

    /// <summary>
    /// The points of a circle of <paramref name="radius"/> around the origin in the XY plane, <paramref name="segments"/>
    /// of them.
    /// </summary>
    private static Vector3[] FlatCircle(int segments, float radius)
    {
        Vector3[] points = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = 2f * Mathf.PI * i / segments;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }

        return points;
    }
}
