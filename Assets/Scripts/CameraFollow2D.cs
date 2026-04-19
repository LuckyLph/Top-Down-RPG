using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector2 offset = Vector2.zero;
    [SerializeField, Min(0f)] private float smoothTimeMoving = 0.08f;
    [SerializeField, Min(0f)] private float smoothTimeIdle = 0.02f;
    [SerializeField, Min(0f)] private float idleVelocityThreshold = 0.01f;
    [SerializeField] private bool snapToPixelGrid = true;
    [SerializeField, Min(1)] private int pixelsPerUnit = 32;

    private Vector3 velocity;
    private Rigidbody2D targetRigidbody;

    private void OnEnable()
    {
        TryAssignTarget();
        velocity = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 targetPosition = targetRigidbody != null
            ? new Vector3(targetRigidbody.position.x, targetRigidbody.position.y, target.position.z)
            : target.position;

        Vector3 desiredPosition = new(
            targetPosition.x + offset.x,
            targetPosition.y + offset.y,
            transform.position.z);

        bool isIdle = targetRigidbody != null && targetRigidbody.linearVelocity.sqrMagnitude <= idleVelocityThreshold * idleVelocityThreshold;
        float smoothTime = isIdle ? smoothTimeIdle : smoothTimeMoving;

        Vector3 nextPosition = smoothTime <= 0f
            ? desiredPosition
            : Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);

        if (snapToPixelGrid)
        {
            float step = 1f / pixelsPerUnit;
            nextPosition.x = Mathf.Round(nextPosition.x / step) * step;
            nextPosition.y = Mathf.Round(nextPosition.y / step) * step;
        }

        transform.position = nextPosition;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRigidbody = target != null ? target.GetComponent<Rigidbody2D>() : null;
        velocity = Vector3.zero;
    }

    private void TryAssignTarget()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            target = player.transform;
            targetRigidbody = target.GetComponent<Rigidbody2D>();
        }
    }
}
