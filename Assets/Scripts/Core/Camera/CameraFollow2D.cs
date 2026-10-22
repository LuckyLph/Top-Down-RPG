using UnityEngine;

/// <summary>
/// Smoothly follows a target (the local player) with the main camera, using a separate smooth time while the
/// target's rigidbody is idle, and optionally snapping the result to the pixel grid. A kick (<see cref="Kick"/>) knocks
/// the camera off the followed position and eases back on <see cref="Time.deltaTime"/>; the smoothing follows on
/// from the followed position, so a kick never shifts where the camera settles.
/// </summary>
public class CameraFollow2D : MonoBehaviour, ICameraKick
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
    private Vector3 followPosition;
    private bool following;
    private Vector2 kick;
    private float kickSeconds;
    private float kickElapsed;
    private bool kickFresh;

    public Transform Target => target;

    /// <summary>
    /// How far the running kick holds the camera off the followed position now, in world units.
    /// </summary>
    public Vector2 KickOffset { get; private set; }

    /// <summary>
    /// Resets the smoothing velocity and the kick so re-enabling does not carry over old momentum, and follows on from
    /// wherever the camera is.
    /// </summary>
    private void OnEnable()
    {
        velocity = Vector3.zero;
        following = false;
        kick = Vector2.zero;
        KickOffset = Vector2.zero;
    }

    /// <summary>
    /// Moves the camera toward the target's position plus the offset after it has moved this frame, using the idle
    /// or moving smooth time; does nothing without a target.
    /// </summary>
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = GetDesiredPosition();

        bool isIdle = targetRigidbody != null && targetRigidbody.linearVelocity.sqrMagnitude <= idleVelocityThreshold * idleVelocityThreshold;
        float smoothTime = isIdle ? smoothTimeIdle : smoothTimeMoving;

        Vector3 current = following ? followPosition : transform.position;
        Vector3 nextPosition = smoothTime <= 0f
            ? desiredPosition
            : Vector3.SmoothDamp(current, desiredPosition, ref velocity, smoothTime);

        followPosition = nextPosition;
        following = true;
        AdvanceKick(Time.deltaTime);
        transform.position = SnapToPixelGrid(nextPosition + (Vector3)KickOffset);
    }

    /// <summary>
    /// Knocks the camera <paramref name="offset"/> world units off the followed position, easing back over
    /// <paramref name="seconds"/> (fast at first, then settling: the offset falls as the cube of the time left).
    /// Ignored for no time, or while the kick under way still holds the camera further off.
    /// </summary>
    public void Kick(Vector2 offset, float seconds)
    {
        if (seconds <= 0f || KickOffset.sqrMagnitude > offset.sqrMagnitude)
        {
            return;
        }

        kick = offset;
        kickSeconds = seconds;
        kickElapsed = 0f;
        kickFresh = true;
        KickOffset = offset;
    }

    /// <summary>
    /// Moves the kick on by <paramref name="deltaTime"/> seconds and returns the offset it holds the camera at now; the
    /// frame a kick starts on shows it whole.
    /// </summary>
    internal Vector2 AdvanceKick(float deltaTime)
    {
        if (kick == Vector2.zero)
        {
            KickOffset = Vector2.zero;
            return KickOffset;
        }

        if (kickFresh)
        {
            kickFresh = false;
        }
        else
        {
            kickElapsed += deltaTime;
        }

        float left = 1f - Mathf.Clamp01(kickElapsed / kickSeconds);
        KickOffset = kick * (left * left * left);
        if (left <= 0f)
        {
            kick = Vector2.zero;
        }

        return KickOffset;
    }

    /// <summary>
    /// Follows <paramref name="newTarget"/> (null stops following), caching its rigidbody for idle detection and
    /// resetting the smoothing velocity.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRigidbody = target != null ? target.GetComponent<Rigidbody2D>() : null;
        velocity = Vector3.zero;
        following = false;
    }

    /// <summary>
    /// Jumps straight to the target without smoothing, e.g. after a spawn or area change; does nothing without a
    /// target.
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null)
        {
            return;
        }

        velocity = Vector3.zero;
        followPosition = GetDesiredPosition();
        following = true;
        transform.position = SnapToPixelGrid(followPosition + (Vector3)KickOffset);
    }

    /// <summary>
    /// The target's position plus the offset, read from its rigidbody when it has one, keeping the camera's own z.
    /// </summary>
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

    /// <summary>
    /// Rounds x and y of <paramref name="position"/> to the nearest pixel at the configured pixels per unit, or
    /// returns it unchanged when snapping is off.
    /// </summary>
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
