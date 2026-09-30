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

    public Transform Target => target;

    private void OnEnable()
    {
        velocity = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = GetDesiredPosition();

        bool isIdle = targetRigidbody != null && targetRigidbody.linearVelocity.sqrMagnitude <= idleVelocityThreshold * idleVelocityThreshold;
        float smoothTime = isIdle ? smoothTimeIdle : smoothTimeMoving;

        Vector3 nextPosition = smoothTime <= 0f
            ? desiredPosition
            : Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);

        transform.position = SnapToPixelGrid(nextPosition);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRigidbody = target != null ? target.GetComponent<Rigidbody2D>() : null;
        velocity = Vector3.zero;
    }

    public void SnapToTarget()
    {
        if (target == null)
        {
            return;
        }

        velocity = Vector3.zero;
        transform.position = SnapToPixelGrid(GetDesiredPosition());
    }

    private Vector3 GetDesiredPosition()
    {
        Vector3 targetPosition = targetRigidbody != null
            ? new Vector3(targetRigidbody.position.x, targetRigidbody.position.y, target.position.z)
            : target.position;

        return new Vector3(
            targetPosition.x + offset.x,
            targetPosition.y + offset.y,
            transform.position.z);
    }

    private Vector3 SnapToPixelGrid(Vector3 position)
    {
        if (!snapToPixelGrid)
        {
            return position;
        }

        float step = 1f / pixelsPerUnit;
        position.x = Mathf.Round(position.x / step) * step;
        position.y = Mathf.Round(position.y / step) * step;
        return position;
    }
}
