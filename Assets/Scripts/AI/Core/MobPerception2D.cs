using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A mob's senses: picks its target from <see cref="IPlayerRegistry"/> with range hysteresis (acquire within the
/// detection radius, keep within the lose distance) and throttled line-of-sight casts against obstacles, and tracks
/// the target's last known position.
/// </summary>
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
    public bool IsTargetHiddenInRange => config != null && !hasLineOfSight && distanceToTarget <= config.LoseTargetDistance;

    /// <summary>
    /// Takes the player registry and config, clears the target, and resets the sight timer and the last known
    /// position to the mob's position. Logs an error when the config sets no obstacle layers, since the mob would then
    /// see through walls.
    /// </summary>
    public void Initialize(IPlayerRegistry playerRegistry, MobConfig mobConfig)
    {
        players = playerRegistry;
        config = mobConfig;
        if (config != null && config.ObstacleLayerMask.value == 0)
        {
            Debug.LogError($"{name}'s {nameof(MobConfig)} {config.name} sets no obstacle layers, so it sees through walls; set them on the asset.", this);
        }

        losTimer = 0f;
        lastKnownTargetPosition = transform.position;
        lastLinecastOrigin = transform.position;
        lastLinecastTarget = transform.position;
        lastLineOfSightBlocked = false;
        ClearTarget();
    }

    /// <summary>
    /// Drops a dead or unregistered target, tracks the current one and, while none is detected, looks for the nearest
    /// visible player. Sight checks run at most every <see cref="MobConfig.LineOfSightInterval"/>.
    /// </summary>
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
            losTimer = Mathf.Max(0.01f, config.LineOfSightInterval);
        }

        if (hasDetectedTarget)
        {
            lastKnownTargetPosition = target.Transform.position;
        }
    }

    /// <summary>
    /// Updates distance and sight for the current target: clears it beyond the lose distance, marks it undetected
    /// outside its range (the lose distance once detected, the detection radius before), and checks line of sight
    /// again when <paramref name="sightDue"/>. Returns whether a sight check ran.
    /// </summary>
    private bool TrackTarget(Vector2 origin, bool sightDue)
    {
        Vector2 targetPosition = target.Transform.position;
        distanceToTarget = Vector2.Distance(origin, targetPosition);

        if (distanceToTarget > config.LoseTargetDistance)
        {
            ClearTarget();
            return false;
        }

        float range = hasDetectedTarget ? config.LoseTargetDistance : config.DetectionRadius;
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

    /// <summary>
    /// Targets the nearest alive player, other than the current target, inside the detection radius with line of
    /// sight. Returns whether any sight check ran, whether or not a target was found.
    /// </summary>
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
            if (distance > config.DetectionRadius || distance >= nearestDistance)
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

    /// <summary>
    /// Whether <paramref name="candidate"/> is alive and still in the player registry.
    /// </summary>
    private bool IsValidTarget(PlayerHandle candidate)
    {
        return candidate.IsAlive && players != null && players.Contains(candidate);
    }

    /// <summary>
    /// Linecasts against the config's obstacle layers and records the cast for the gizmos. True when nothing blocks it
    /// or no obstacle layers are set.
    /// </summary>
    private bool EvaluateLineOfSight(Vector2 origin, Vector2 targetPosition)
    {
        lastLinecastOrigin = origin;
        lastLinecastTarget = targetPosition;

        if (config == null || config.ObstacleLayerMask.value == 0)
        {
            lastLineOfSightBlocked = false;
            return true;
        }

        RaycastHit2D hit = Physics2D.Linecast(origin, targetPosition, config.ObstacleLayerMask);
        lastLineOfSightBlocked = hit.collider != null;
        return !lastLineOfSightBlocked;
    }

    /// <summary>
    /// Forgets the target and its sight state; the last known position is kept.
    /// </summary>
    private void ClearTarget()
    {
        target = null;
        hasDetectedTarget = false;
        hasLineOfSight = false;
        distanceToTarget = float.PositiveInfinity;
    }

    /// <summary>
    /// Draws the detection and lose ranges, the last sight line colored by whether it was blocked, and the last known
    /// target position.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        float detectionRadius = config != null ? config.DetectionRadius : 0f;
        float loseRadius = config != null ? config.LoseTargetDistance : 0f;
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
