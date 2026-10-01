using System.Collections.Generic;
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

    private IPlayerRegistry players;
    private PlayerHandle target;
    private MobConfig config;
    private float losTimer;
    private bool hasDetectedTarget;
    private bool hasLineOfSight;
    private float distanceToTarget;
    private Vector2 lastKnownTargetPosition;
    private Vector2 lastLinecastOrigin;
    private Vector2 lastLinecastTarget;
    private bool lastLineOfSightBlocked;

    public PlayerHandle CurrentTargetPlayer => target;
    public Transform CurrentTarget => target != null ? target.Transform : null;
    public bool HasDetectedTarget => hasDetectedTarget;
    public bool HasLineOfSight => hasLineOfSight;
    public float DistanceToTarget => distanceToTarget;
    public Vector2 LastKnownTargetPosition => lastKnownTargetPosition;
    public bool IsTargetHiddenInRange => config != null && !hasLineOfSight && distanceToTarget <= config.loseTargetDistance;

    public void Initialize(IPlayerRegistry playerRegistry, MobConfig mobConfig)
    {
        players = playerRegistry;
        config = mobConfig;
        losTimer = 0f;
        lastKnownTargetPosition = transform.position;
        lastLinecastOrigin = transform.position;
        lastLinecastTarget = transform.position;
        lastLineOfSightBlocked = false;
        ClearTarget();
    }

    public void Tick(float deltaTime)
    {
        if (config == null)
        {
            return;
        }

        if (target != null && !IsValidTarget(target))
        {
            ClearTarget();
        }

        losTimer -= Mathf.Max(0f, deltaTime);
        bool sightDue = losTimer <= 0f;
        Vector2 origin = transform.position;
        bool castLine = false;

        if (target != null)
        {
            castLine = TrackTarget(origin, sightDue);
        }

        if (!hasDetectedTarget && sightDue && TryAcquireTarget(origin))
        {
            castLine = true;
        }

        if (castLine)
        {
            losTimer = Mathf.Max(0.01f, config.lineOfSightInterval);
        }

        if (hasDetectedTarget)
        {
            lastKnownTargetPosition = target.Transform.position;
        }
    }

    private bool TrackTarget(Vector2 origin, bool sightDue)
    {
        Vector2 targetPosition = target.Transform.position;
        distanceToTarget = Vector2.Distance(origin, targetPosition);

        if (distanceToTarget > config.loseTargetDistance)
        {
            ClearTarget();
            return false;
        }

        float range = hasDetectedTarget ? config.loseTargetDistance : config.detectionRadius;
        if (distanceToTarget > range)
        {
            hasDetectedTarget = false;
            hasLineOfSight = false;
            return false;
        }

        if (sightDue)
        {
            hasLineOfSight = EvaluateLineOfSight(origin, targetPosition);
        }

        hasDetectedTarget = hasLineOfSight;
        return sightDue;
    }

    private bool TryAcquireTarget(Vector2 origin)
    {
        if (players == null)
        {
            return false;
        }

        IReadOnlyList<PlayerHandle> candidates = players.Players;
        PlayerHandle nearest = null;
        float nearestDistance = float.PositiveInfinity;
        bool castLine = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            PlayerHandle candidate = candidates[i];
            if (candidate == target || !candidate.IsAlive)
            {
                continue;
            }

            Vector2 candidatePosition = candidate.Transform.position;
            float distance = Vector2.Distance(origin, candidatePosition);
            if (distance > config.detectionRadius || distance >= nearestDistance)
            {
                continue;
            }

            castLine = true;
            if (EvaluateLineOfSight(origin, candidatePosition))
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        if (nearest != null)
        {
            target = nearest;
            distanceToTarget = nearestDistance;
            hasLineOfSight = true;
            hasDetectedTarget = true;
        }

        return castLine;
    }

    private bool IsValidTarget(PlayerHandle candidate)
    {
        return candidate.IsAlive && players != null && players.Contains(candidate);
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

    private void ClearTarget()
    {
        target = null;
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
