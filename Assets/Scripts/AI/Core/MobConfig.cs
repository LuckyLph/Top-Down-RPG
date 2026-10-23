using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A mob type's tuning: movement, perception, search, combat, crowd, patrol, leash, respawn and navigation. Read-only at runtime;
/// tests build their own with <see cref="Create"/>.
/// </summary>
[CreateAssetMenu(menuName = "AI/Mob Config", fileName = "MobConfig")]
public class MobConfig : ScriptableObject
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 2.5f;
    [SerializeField, Min(0f)] private float acceleration = 20f;
    [SerializeField, Min(0.01f)] private float waypointReachDistance = 0.05f;
    [SerializeField, Min(0.01f)] private float arrivalDistance = 0.15f;

    [Header("Perception")]
    [SerializeField, Min(0f)] private float detectionRadius = 6f;
    [SerializeField, Min(0f)] private float loseTargetDistance = 8f;
    [SerializeField, Min(0.01f)] private float lineOfSightInterval = 0.2f;
    [SerializeField] private LayerMask obstacleLayerMask = 1 << 8;
    [SerializeField, Min(0f), Tooltip("How long a mob waits at the last known position of a target that slipped out of sight in range. The walk there is not included.")]
    private float searchDuration = 3f;

    [Header("Combat")]
    [SerializeField, Min(0f)] private float attackStopDistance = 1.25f;
    [SerializeField, Min(0f)] private float attackExitBuffer = 0.25f;
    [SerializeField, Min(0)] private int attackDamage = 1;
    [SerializeField] private DamageType attackDamageType = DamageType.Physical;
    [SerializeField] private StatusEffectDefinition[] attackStatuses = new StatusEffectDefinition[0];
    [SerializeField, Min(0f), Tooltip("The attack's cooldown, from its start. An attack that ends before its contact gives it back.")]
    private float attackInterval = 0.75f;
    [SerializeField, Min(0f), Tooltip("The attack's windup, its telegraph: the mob holds still, its direction locked, and a player who leaves its area before contact takes nothing.")]
    private float attackWindup = 0.45f;
    [SerializeField, Min(0.01f), Tooltip("The attack's contact, from the moment its hit resolves.")]
    private float attackContact = 0.05f;
    [SerializeField, Min(0f), Tooltip("The attack's recovery after its contact; the mob still holds still.")]
    private float attackRecovery = 0.25f;
    [SerializeField, Tooltip("The attack's area at contact: its width across the attack and its depth along it, from the edge of the mob's collider. Every unit in it is hit, under the faction rules.")]
    private Vector2 attackArea = new(1f, 0.75f);
    [SerializeField, Tooltip("The attack's clip in the mob's character animation set.")]
    private string attackClip = "";

    [Header("Crowd")]
    [SerializeField, Min(0f), Tooltip("Mobs closer than this (center to center) steer apart; 0 disables separation.")]
    private float separationRadius = 0.8f;
    [SerializeField, Min(0f), Tooltip("Push speed at full overlap, in units per second. Keep it above the move speed: a mob pushing into a crowd settles at about separation radius x (1 - move speed / this) from its neighbours.")]
    private float separationStrength = 4f;
    [SerializeField, Min(0f), Tooltip("Within this distance of its target, a chasing mob with another mob directly ahead waits instead of pushing.")]
    private float crowdWaitDistance = 2f;

    [Header("Patrol")]
    [SerializeField, Min(0f)] private float patrolRoamRadius = 4f;
    [SerializeField, Min(1)] private int patrolSampleAttempts = 12;
    [SerializeField] private Vector2 idleDurationRange = new(0.5f, 1.5f);

    [Header("Leash")]
    [SerializeField, Min(0), Tooltip("How many cells beyond its home chunk's edge a mob in a world chases before it gives up and walks home. Leash plus the detection radius must stay under the world streaming's load extents, and leash plus the lose distance under its keep extents, so a mob near a player always has its home chunk loaded (Docs/WorldStreaming.md: Leash). Mobs in areas have no home chunk and no leash.")]
    private int leashCells = 8;

    [Header("Respawn")]
    [SerializeField, Tooltip("Whether a mob killed in a world comes back. Off for mobs that stay dead, such as bosses.")]
    private bool respawns = true;
    [SerializeField, Min(0f), Tooltip("Seconds of game time after its death before a killed mob in a world comes back. It returns only when its chunk loads again, never in front of a player, so it may stay away longer (Docs/WorldStreaming.md: Persistence).")]
    private float respawnDelaySeconds = 300f;

    [Header("Navigation")]
    [SerializeField, Min(0.01f)] private float repathInterval = 0.25f;
    [SerializeField, Min(1)] private int nearestCellSearchRadius = 8;
    [SerializeField, Min(0.1f), Tooltip("Patrol and return give up after following a path this long without making headway.")]
    private float stuckTimeout = 1f;
    [SerializeField] private TerrainMovementProfile2D movementProfile;

    public float MoveSpeed => moveSpeed;
    public float Acceleration => acceleration;
    public float WaypointReachDistance => waypointReachDistance;
    public float ArrivalDistance => arrivalDistance;
    public float DetectionRadius => detectionRadius;
    public float LoseTargetDistance => loseTargetDistance;
    public float LineOfSightInterval => lineOfSightInterval;
    public LayerMask ObstacleLayerMask => obstacleLayerMask;
    public float SearchDuration => searchDuration;
    public float AttackStopDistance => attackStopDistance;
    public float AttackExitBuffer => attackExitBuffer;
    public int AttackDamage => attackDamage;
    public DamageType AttackDamageType => attackDamageType;
    public IReadOnlyList<StatusEffectDefinition> AttackStatuses => attackStatuses;
    public float AttackInterval => attackInterval;
    public AttackTimeline AttackTimeline => new(attackWindup, Mathf.Max(0.01f, attackContact), attackRecovery);
    public Vector2 AttackArea => attackArea;
    public string AttackClip => attackClip;
    public float SeparationRadius => separationRadius;
    public float SeparationStrength => separationStrength;
    public float CrowdWaitDistance => crowdWaitDistance;
    public float PatrolRoamRadius => patrolRoamRadius;
    public int PatrolSampleAttempts => patrolSampleAttempts;
    public Vector2 IdleDurationRange => idleDurationRange;
    public int LeashCells => leashCells;
    public bool Respawns => respawns;
    public float RespawnDelaySeconds => respawnDelaySeconds;
    public float RepathInterval => repathInterval;
    public int NearestCellSearchRadius => nearestCellSearchRadius;
    public float StuckTimeout => stuckTimeout;
    public TerrainMovementProfile2D MovementProfile => movementProfile;

    /// <summary>
    /// Rolls how long a mob idles before patrolling, uniformly within <see cref="IdleDurationRange"/>, whose bounds
    /// may be in either order.
    /// </summary>
    public float NextIdleDuration(IRandom random)
    {
        float min = Mathf.Min(idleDurationRange.x, idleDurationRange.y);
        float max = Mathf.Max(idleDurationRange.x, idleDurationRange.y);
        return random.Range(min, max);
    }

    /// <summary>
    /// Builds a config in code, for tests. Every value left out keeps the asset's default.
    /// </summary>
    public static MobConfig Create(
        float? moveSpeed = null,
        float? waypointReachDistance = null,
        float? arrivalDistance = null,
        float? detectionRadius = null,
        float? loseTargetDistance = null,
        float? attackStopDistance = null,
        float? attackExitBuffer = null,
        int? attackDamage = null,
        DamageType? attackDamageType = null,
        StatusEffectDefinition[] attackStatuses = null,
        float? attackInterval = null,
        float? attackWindup = null,
        float? attackContact = null,
        float? attackRecovery = null,
        Vector2? attackArea = null,
        string attackClip = null,
        float? patrolRoamRadius = null,
        Vector2? idleDurationRange = null,
        float? repathInterval = null,
        TerrainMovementProfile2D movementProfile = null,
        int? leashCells = null,
        bool? respawns = null,
        float? respawnDelaySeconds = null)
    {
        MobConfig config = CreateInstance<MobConfig>();
        config.moveSpeed = moveSpeed ?? config.moveSpeed;
        config.waypointReachDistance = waypointReachDistance ?? config.waypointReachDistance;
        config.arrivalDistance = arrivalDistance ?? config.arrivalDistance;
        config.detectionRadius = detectionRadius ?? config.detectionRadius;
        config.loseTargetDistance = loseTargetDistance ?? config.loseTargetDistance;
        config.attackStopDistance = attackStopDistance ?? config.attackStopDistance;
        config.attackExitBuffer = attackExitBuffer ?? config.attackExitBuffer;
        config.attackDamage = attackDamage ?? config.attackDamage;
        config.attackDamageType = attackDamageType ?? config.attackDamageType;
        config.attackStatuses = attackStatuses ?? config.attackStatuses;
        config.attackInterval = attackInterval ?? config.attackInterval;
        config.attackWindup = attackWindup ?? config.attackWindup;
        config.attackContact = attackContact ?? config.attackContact;
        config.attackRecovery = attackRecovery ?? config.attackRecovery;
        config.attackArea = attackArea ?? config.attackArea;
        config.attackClip = attackClip ?? config.attackClip;
        config.patrolRoamRadius = patrolRoamRadius ?? config.patrolRoamRadius;
        config.idleDurationRange = idleDurationRange ?? config.idleDurationRange;
        config.repathInterval = repathInterval ?? config.repathInterval;
        config.leashCells = leashCells ?? config.leashCells;
        config.respawns = respawns ?? config.respawns;
        config.respawnDelaySeconds = respawnDelaySeconds ?? config.respawnDelaySeconds;
        if (movementProfile != null)
        {
            config.movementProfile = movementProfile;
        }

        return config;
    }
}
