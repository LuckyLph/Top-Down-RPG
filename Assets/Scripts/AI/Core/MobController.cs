using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(MobMotor2D))]
[RequireComponent(typeof(MobPerception2D))]
[RequireComponent(typeof(MobPathAgent2D))]
[RequireComponent(typeof(MobPatrolAnchor))]
[RequireComponent(typeof(MeleeDamageDealer))]
public class MobController : MonoBehaviour
{
    [SerializeField] private MobConfig config;
    [SerializeField] private NavigationGrid2D navigationGrid;
    [SerializeField] private MobTargetProvider targetProvider;
    [SerializeField] private bool autoResolveDependencies = true;
    [SerializeField] private UnityEvent onAttackRangeEntered;
    [Header("Debug")]
    [SerializeField] private bool drawBrainGizmos = true;
    [SerializeField] private bool drawStateLabel = true;
    [SerializeField] private Color idleColor = new(0.8f, 0.8f, 0.8f, 0.9f);
    [SerializeField] private Color patrolColor = new(0.5f, 0.7f, 1f, 0.9f);
    [SerializeField] private Color chaseColor = new(1f, 0.35f, 0.35f, 0.95f);
    [SerializeField] private Color attackColor = new(1f, 0.9f, 0.2f, 0.95f);
    [SerializeField] private Color returnColor = new(0.4f, 1f, 0.6f, 0.95f);

    private readonly Dictionary<MobStateId, IMobState> states = new();

    private MobMotor2D motor;
    private MobPerception2D perception;
    private MobPathAgent2D pathAgent;
    private MobPatrolAnchor patrol;
    private MeleeDamageDealer damageDealer;
    private Collider2D selfCollider;
    private IMobState currentState;
    private bool initialized;
    private float deltaTime;

    public MobConfig Config => config;
    public NavigationGrid2D NavigationGrid => navigationGrid;
    public MobTargetProvider TargetProvider => targetProvider;
    public MobMotor2D Motor => motor;
    public MobPerception2D Perception => perception;
    public MobPathAgent2D PathAgent => pathAgent;
    public MobPatrolAnchor Patrol => patrol;
    public MeleeDamageDealer DamageDealer => damageDealer;
    public MobStateId CurrentStateId => currentState != null ? currentState.StateId : MobStateId.Idle;
    public float DeltaTime => deltaTime;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        EnsureInitialized();

        if (currentState == null)
        {
            ChangeState(MobStateId.Idle);
        }
    }

    private void Update()
    {
        TickStateMachine(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        FixedTickStateMachine();
    }

    public void Configure(MobConfig mobConfig, NavigationGrid2D navGrid, MobTargetProvider provider)
    {
        config = mobConfig;
        navigationGrid = navGrid;
        targetProvider = provider;
        autoResolveDependencies = false;
        initialized = false;
        EnsureInitialized();
    }

    public void TickStateMachine(float dt)
    {
        EnsureInitialized();

        deltaTime = Mathf.Max(0f, dt);
        perception.Tick(deltaTime);
        currentState?.Tick();
    }

    public void FixedTickStateMachine()
    {
        EnsureInitialized();

        currentState?.FixedTick();
        motor.FixedTick();
    }

    public void ChangeState(MobStateId nextStateId)
    {
        EnsureInitialized();

        if (!states.TryGetValue(nextStateId, out IMobState nextState))
        {
            Debug.LogError($"MobBrain is missing state '{nextStateId}'.", this);
            return;
        }

        if (currentState == nextState)
        {
            return;
        }

        currentState?.Exit();
        currentState = nextState;
        currentState.Enter();
    }

    public void InvokeAttackRangeEntered()
    {
        onAttackRangeEntered?.Invoke();
    }

    public bool IsTargetInAttackRange()
    {
        return TryGetAttackDistanceToTarget(out float distance) && distance <= Config.attackStopDistance;
    }

    public bool ShouldExitAttackRange()
    {
        return !TryGetAttackDistanceToTarget(out float distance) || distance > Config.attackStopDistance + Config.attackExitBuffer;
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        CacheComponents();

        if (autoResolveDependencies)
        {
            ResolveDependencies();
        }

        if (config == null)
        {
            config = CreateRuntimeFallbackConfig();
        }

        if (navigationGrid != null && !navigationGrid.IsBuilt)
        {
            navigationGrid.BuildGrid();
        }

        motor.Initialize(config);
        pathAgent.Initialize(navigationGrid, motor, config);
        perception.Initialize(targetProvider, config);
        patrol.Initialize(navigationGrid, config);
        damageDealer.Initialize(config);

        RegisterStates();
        initialized = true;
    }

    private void CacheComponents()
    {
        if (motor == null)
        {
            motor = GetComponent<MobMotor2D>();
        }

        if (perception == null)
        {
            perception = GetComponent<MobPerception2D>();
        }

        if (pathAgent == null)
        {
            pathAgent = GetComponent<MobPathAgent2D>();
        }

        if (patrol == null)
        {
            patrol = GetComponent<MobPatrolAnchor>();
        }

        if (damageDealer == null)
        {
            damageDealer = GetComponent<MeleeDamageDealer>();
        }

        if (selfCollider == null)
        {
            selfCollider = GetComponent<Collider2D>();
        }
    }

    private void ResolveDependencies()
    {
        if (navigationGrid == null)
        {
            navigationGrid = FindAnyObjectByType<NavigationGrid2D>();
        }

        if (targetProvider == null)
        {
            targetProvider = FindAnyObjectByType<MobTargetProvider>();
        }
    }

    private void RegisterStates()
    {
        states.Clear();
        states[MobStateId.Idle] = new IdleState(this);
        states[MobStateId.Patrol] = new PatrolRoamState(this);
        states[MobStateId.Chase] = new ChaseState(this);
        states[MobStateId.AttackRange] = new AttackRangeState(this);
        states[MobStateId.Return] = new ReturnToSpawnState(this);
    }

    private static MobConfig CreateRuntimeFallbackConfig()
    {
        MobConfig runtimeConfig = ScriptableObject.CreateInstance<MobConfig>();
        runtimeConfig.hideFlags = HideFlags.HideAndDontSave;
        return runtimeConfig;
    }

    private bool TryGetAttackDistanceToTarget(out float distance)
    {
        distance = float.PositiveInfinity;

        if (config == null || perception == null || perception.CurrentTarget == null)
        {
            return false;
        }

        Transform target = perception.CurrentTarget;
        if (selfCollider != null && target.TryGetComponent(out Collider2D targetCollider))
        {
            // Edge-to-edge distance avoids scale/collider-size issues from center-distance checks.
            ColliderDistance2D colliderDistance = selfCollider.Distance(targetCollider);
            distance = Mathf.Max(0f, colliderDistance.distance);
            return true;
        }

        distance = Vector2.Distance(transform.position, target.position);
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawBrainGizmos)
        {
            return;
        }

        Color stateColor = GetStateColor(CurrentStateId);
        Vector3 origin = transform.position;

        Gizmos.color = stateColor;
        Gizmos.DrawWireSphere(origin, 0.16f);

        if (perception != null && perception.CurrentTarget != null)
        {
            Gizmos.DrawLine(origin, perception.CurrentTarget.position);
        }

        if (patrol != null)
        {
            Gizmos.color = returnColor;
            Gizmos.DrawLine(origin, patrol.SpawnPosition);
        }

#if UNITY_EDITOR
        if (drawStateLabel)
        {
            Handles.color = stateColor;
            Handles.Label(origin + Vector3.up * 0.4f, $"[{CurrentStateId}]");
        }
#endif
    }

    private Color GetStateColor(MobStateId stateId)
    {
        return stateId switch
        {
            MobStateId.Idle => idleColor,
            MobStateId.Patrol => patrolColor,
            MobStateId.Chase => chaseColor,
            MobStateId.AttackRange => attackColor,
            MobStateId.Return => returnColor,
            _ => Color.white
        };
    }
}
