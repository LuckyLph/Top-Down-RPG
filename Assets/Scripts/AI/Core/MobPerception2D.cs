using UnityEngine;

public class MobPerception2D : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color detectionRangeColor = new(0.25f, 0.8f, 1f, 0.7f);
    [SerializeField] private Color loseRangeColor = new(1f, 0.7f, 0.25f, 0.65f);
    [SerializeField] private Color lineOfSightClearColor = new(0.1f, 1f, 0.1f, 0.9f);
    [SerializeField] private Color lineOfSightBlockedColor = new(1f, 0.2f, 0.2f, 0.9f);
    [SerializeField] private Color lastKnownTargetColor = new(1f, 1f, 0.3f, 0.75f);

    private IPlayerLocator player;
    private MobConfig config;
    private float losTimer;
    private bool hasDetectedTarget;
    private bool hasLineOfSight;
    private float distanceToTarget;
    private Vector2 lastKnownTargetPosition;
    private Vector2 lastLinecastOrigin;
    private Vector2 lastLinecastTarget;
    private bool lastLineOfSightBlocked;

    public Transform CurrentTarget => player?.Transform;
    public bool HasDetectedTarget => hasDetectedTarget;
    public bool HasLineOfSight => hasLineOfSight;
    public float DistanceToTarget => distanceToTarget;
    public Vector2 LastKnownTargetPosition => lastKnownTargetPosition;

    public void Initialize(IPlayerLocator playerLocator, MobConfig mobConfig)
    {
        player = playerLocator;
        config = mobConfig;
        losTimer = 0f;
        hasDetectedTarget = false;
        hasLineOfSight = false;
        distanceToTarget = float.PositiveInfinity;
        lastKnownTargetPosition = transform.position;
        lastLinecastOrigin = transform.position;
        lastLinecastTarget = transform.position;
        lastLineOfSightBlocked = false;
    }

    public void Tick(float deltaTime)
    {
        if (config == null)
        {
            return;
        }

        Transform target = CurrentTarget;
        if (target == null || !player.IsAlive)
        {
            ResetPerception();
            return;
        }

        Vector2 origin = transform.position;
        Vector2 targetPosition = target.position;
        distanceToTarget = Vector2.Distance(origin, targetPosition);

        float range = hasDetectedTarget ? config.loseTargetDistance : config.detectionRadius;
        bool inRange = distanceToTarget <= range;

        if (!inRange)
        {
            hasDetectedTarget = false;
            hasLineOfSight = false;
            return;
        }

        losTimer -= Mathf.Max(0f, deltaTime);
        if (losTimer <= 0f)
        {
            hasLineOfSight = EvaluateLineOfSight(origin, targetPosition);
            losTimer = Mathf.Max(0.01f, config.lineOfSightInterval);
        }

        hasDetectedTarget = inRange && hasLineOfSight;
        if (hasDetectedTarget)
        {
            lastKnownTargetPosition = targetPosition;
        }
    }

    private bool EvaluateLineOfSight(Vector2 origin, Vector2 targetPosition)
    {
        lastLinecastOrigin = origin;
        lastLinecastTarget = targetPosition;

        if (config == null || config.obstacleLayerMask.value == 0)
        {
            lastLineOfSightBlocked = false;
            return true;
        }

        RaycastHit2D hit = Physics2D.Linecast(origin, targetPosition, config.obstacleLayerMask);
        lastLineOfSightBlocked = hit.collider != null;
        return !lastLineOfSightBlocked;
    }

    private void ResetPerception()
    {
        hasDetectedTarget = false;
        hasLineOfSight = false;
        distanceToTarget = float.PositiveInfinity;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        float detectionRadius = config != null ? config.detectionRadius : 0f;
        float loseRadius = config != null ? config.loseTargetDistance : 0f;
        Vector3 position = transform.position;

        if (detectionRadius > 0f)
        {
            Gizmos.color = detectionRangeColor;
            Gizmos.DrawWireSphere(position, detectionRadius);
        }

        if (loseRadius > 0f)
        {
            Gizmos.color = loseRangeColor;
            Gizmos.DrawWireSphere(position, loseRadius);
        }

        if (CurrentTarget != null)
        {
            Gizmos.color = lastLineOfSightBlocked ? lineOfSightBlockedColor : lineOfSightClearColor;
            Gizmos.DrawLine(lastLinecastOrigin, lastLinecastTarget);
        }

        Gizmos.color = lastKnownTargetColor;
        Gizmos.DrawWireSphere(lastKnownTargetPosition, 0.12f);
    }
}
