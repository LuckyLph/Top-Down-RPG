using UnityEngine;

/// <summary>
/// Tuning for click-to-move: movement, path following, order timing and pointer targeting.
/// Referenced by the player prefab; the Gameplay scope registers the prefab's instance.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Player Control Settings", fileName = "PlayerControlSettings")]
public sealed class PlayerControlSettings : ScriptableObject
{
    [Header("Movement")]
    [SerializeField, Min(0f), Tooltip("Units per second.")]
    private float moveSpeed = 5f;

    [SerializeField, Min(0.01f), Tooltip("Distance at which an intermediate waypoint counts as reached.")]
    private float waypointReachDistance = 0.1f;

    [SerializeField, Min(0.01f), Tooltip("Distance at which the final waypoint counts as reached.")]
    private float arrivalDistance = 0.1f;

    [SerializeField, Tooltip("Terrain walkability and costs for player paths. Empty uses the grid's defaults.")]
    private TerrainMovementProfile2D movementProfile;

    [SerializeField, Min(0f), Tooltip("Seconds without progress after which a move order gives up.")]
    private float stuckTimeout = 0.75f;

    [Header("Orders")]
    [SerializeField, Min(0.01f), Tooltip("Seconds between re-evaluations of the cursor while the move button is held.")]
    private float holdReevaluateInterval = 0.15f;

    [SerializeField, Min(0.01f), Tooltip("Seconds between repaths while chasing an attack target.")]
    private float repathInterval = 0.5f;

    [SerializeField, Min(0f), Tooltip("Distance an attack target must move from the last path goal to trigger an early repath.")]
    private float targetMoveRepathDistance = 0.5f;

    [SerializeField, Min(0f), Tooltip("Seconds a buffered cast waits for the running cast to end before it is dropped.")]
    private float castBufferWindow = 0.4f;

    [Header("Targeting")]
    [SerializeField, Min(0f), Tooltip("Radius around the cursor in which a unit can be clicked.")]
    private float pickRadius = 0.35f;

    [SerializeField, Tooltip("Layers of units that can be attacked (mobs).")]
    private LayerMask enemyLayers;

    [SerializeField, Tooltip("Layers of units that count as allies (players).")]
    private LayerMask allyLayers;

    public float MoveSpeed => moveSpeed;
    public float WaypointReachDistance => waypointReachDistance;
    public float ArrivalDistance => arrivalDistance;
    public TerrainMovementProfile2D MovementProfile => movementProfile;
    public float StuckTimeout => stuckTimeout;
    public float HoldReevaluateInterval => holdReevaluateInterval;
    public float RepathInterval => repathInterval;
    public float TargetMoveRepathDistance => targetMoveRepathDistance;
    public float CastBufferWindow => castBufferWindow;
    public float PickRadius => pickRadius;
    public LayerMask EnemyLayers => enemyLayers;
    public LayerMask AllyLayers => allyLayers;

    public PathFollowerSettings2D FollowerSettings =>
        new(waypointReachDistance, arrivalDistance, useStraightLineShortcut: true);
}
