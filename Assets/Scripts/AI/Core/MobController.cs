using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using VContainer;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A mob's brain: initializes its sibling components from its <see cref="MobConfig"/>, owns the six
/// <see cref="IMobState"/>s and the <see cref="MobSeparation2D"/>, and runs the state machine from Update and
/// FixedUpdate, ticking the <see cref="MeleeDamageDealer"/>'s attack timeline each frame and playing the attacks it
/// starts and cancels on the <see cref="MobMotor2D"/>. A stun cancels a running attack; the mob holds still through
/// one. States reach the mob's components, config and attack-range checks through it. <see cref="MobNetworkSync"/>
/// disables it on clients, so the AI never runs there.
/// </summary>
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
    [SerializeField] private UnityEvent onAttackRangeEntered;
    [Header("Debug")]
    [SerializeField] private bool drawBrainGizmos = true;
    [SerializeField] private bool drawStateLabel = true;
    [SerializeField] private Color idleColor = new(0.8f, 0.8f, 0.8f, 0.9f);
    [SerializeField] private Color patrolColor = new(0.5f, 0.7f, 1f, 0.9f);
    [SerializeField] private Color chaseColor = new(1f, 0.35f, 0.35f, 0.95f);
    [SerializeField] private Color attackColor = new(1f, 0.9f, 0.2f, 0.95f);
    [SerializeField] private Color returnColor = new(0.4f, 1f, 0.6f, 0.95f);
    [SerializeField] private Color searchColor = new(1f, 0.6f, 0.2f, 0.95f);

    private readonly Dictionary<MobStateId, IMobState> states = new();

    private INavigation2D navigation;
    private IPlayerRegistry players;
    private IRandom random;
    private MobMotor2D motor;
    private MobPerception2D perception;
    private MobPathAgent2D pathAgent;
    private MobPatrolAnchor patrol;
    private MeleeDamageDealer damageDealer;
    private Collider2D selfCollider;
    private Transform cachedTarget;
    private Collider2D cachedTargetCollider;
    private MobSeparation2D separation;
    private StatusEffects statuses;
    private IMobState currentState;
    private bool initialized;
    private float deltaTime;

    public MobConfig Config => config;
    public INavigation2D Navigation => navigation;
    public IPlayerRegistry Players => players;
    public IRandom Random => random;
    public MobMotor2D Motor => motor;
    public MobPerception2D Perception => perception;
    public MobPathAgent2D PathAgent => pathAgent;
    public MobPatrolAnchor Patrol => patrol;
    public MeleeDamageDealer DamageDealer => damageDealer;
    public MobSeparation2D Separation => separation;
    public MobStateId CurrentStateId => currentState != null ? currentState.StateId : MobStateId.Idle;
    public float DeltaTime => deltaTime;
    public bool IsStunned => statuses != null && (statuses.Controls & StatusControls.Stun) != 0;

    /// <summary>
    /// Caches the components and initializes the mob from its config, disabling the brain when none is assigned.
    /// </summary>
    private void Awake()
    {
        EnsureInitialized();
    }

    /// <summary>
    /// Logs an error when the mob was never given its navigation, players and random source, then enters Idle
    /// unless a state is already running.
    /// </summary>
    private void Start()
    {
        EnsureInitialized();

        if (navigation == null || players == null || random == null)
        {
            Debug.LogError($"{name} was not injected with its navigation, players and random source; spawn mobs through a {nameof(MobSpawnPoint)} or resolver.Instantiate.", this);
        }

        if (currentState == null)
        {
            ChangeState(MobStateId.Idle);
        }
    }

    /// <summary>
    /// Plays the damage dealer's attacks on the motor while the brain runs.
    /// </summary>
    private void OnEnable()
    {
        CacheComponents();
        damageDealer.AttackStarted += HandleAttackStarted;
        damageDealer.AttackCancelled += HandleAttackCancelled;
    }

    /// <summary>
    /// Stops playing the damage dealer's attacks.
    /// </summary>
    private void OnDisable()
    {
        if (damageDealer != null)
        {
            damageDealer.AttackStarted -= HandleAttackStarted;
            damageDealer.AttackCancelled -= HandleAttackCancelled;
        }
    }

    /// <summary>
    /// Ticks perception and the current state with the frame's delta time.
    /// </summary>
    private void Update()
    {
        TickStateMachine(Time.deltaTime);
    }

    /// <summary>
    /// Runs the physics-step half of the brain: status speed scale, separation, state movement and the motor.
    /// </summary>
    private void FixedUpdate()
    {
        FixedTickStateMachine();
    }

    /// <summary>
    /// Injects its navigation (the area's grid, or its world's navigation), its players (every player in an area, the
    /// players in its world in a world, <see cref="WorldPlayers"/>) and the random source, and rebuilds the mob with
    /// them when it has already initialized.
    /// </summary>
    [Inject]
    public void Construct(INavigation2D mobNavigation, IPlayerRegistry playerRegistry, IRandom randomSource)
    {
        navigation = mobNavigation;
        players = playerRegistry;
        random = randomSource;
        Reinitialize();
    }

    /// <summary>
    /// Setup without dependency injection, used by tests: sets the config and dependencies, then rebuilds the mob,
    /// re-entering Idle when a state was running.
    /// </summary>
    public void Configure(MobConfig mobConfig, INavigation2D mobNavigation, IPlayerRegistry playerRegistry, IRandom randomSource)
    {
        config = mobConfig;
        navigation = mobNavigation;
        players = playerRegistry;
        random = randomSource;
        Rebuild();
    }

    /// <summary>
    /// Gives a mob of a world its home, its home chunk's area in world units: it patrols inside it and gives up on a
    /// target or a path beyond its leash (<see cref="MobPatrolAnchor.SetHome"/>). Mobs of areas have none.
    /// </summary>
    public void SetHome(Rect homeArea)
    {
        CacheComponents();
        patrol.SetHome(homeArea);
    }

    /// <summary>
    /// Rebuilds the mob when it has already initialized; otherwise the first <see cref="EnsureInitialized"/> picks up
    /// the new dependencies.
    /// </summary>
    private void Reinitialize()
    {
        if (!initialized)
        {
            return;
        }

        Rebuild();
    }

    /// <summary>
    /// Exits the current state, initializes every component again from the current config and dependencies, and
    /// re-enters Idle when a state was running.
    /// </summary>
    private void Rebuild()
    {
        bool wasRunning = currentState != null;
        currentState?.Exit();
        currentState = null;
        initialized = false;
        EnsureInitialized();

        if (wasRunning && initialized)
        {
            ChangeState(MobStateId.Idle);
        }
    }

    /// <summary>
    /// Per-frame step: ticks perception with <paramref name="dt"/> (clamped at 0), moves the running attack on (a stun
    /// cancels it instead), then ticks the current state unless the mob is stunned. Public so tests can drive the
    /// brain.
    /// </summary>
    public void TickStateMachine(float dt)
    {
        EnsureInitialized();

        deltaTime = Mathf.Max(0f, dt);
        perception.Tick(deltaTime);
        if (IsStunned)
        {
            damageDealer.CancelAttack();
            return;
        }

        damageDealer.Tick();
        currentState?.Tick();
    }

    /// <summary>
    /// Physics step: sets the motor's speed scale from the statuses (0 while an attack runs, so the mob holds still
    /// through it), senses neighbours, fixed-ticks the current state (or stops the motor while stunned), then adds the
    /// separation steering and steps the motor.
    /// </summary>
    public void FixedTickStateMachine()
    {
        EnsureInitialized();

        motor.SetSpeedScale(damageDealer.IsAttacking ? 0f : statuses != null ? statuses.MovementScale : 1f);
        separation.Sense(motor.Position);
        if (IsStunned)
        {
            motor.Stop();
        }
        else
        {
            currentState?.FixedTick();
        }

        motor.SetSteeringVelocity(separation.Velocity);
        motor.FixedTick();
    }

    /// <summary>
    /// Exits the current state and enters <paramref name="nextStateId"/>. Does nothing when it is already current, and
    /// logs an error for an id with no registered state.
    /// </summary>
    public void ChangeState(MobStateId nextStateId)
    {
        EnsureInitialized();

        if (!states.TryGetValue(nextStateId, out IMobState nextState))
        {
            Debug.LogError($"MobController is missing state '{nextStateId}'.", this);
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

    /// <summary>
    /// Plays an attack the damage dealer just started on the motor, toward its locked direction on its timeline.
    /// </summary>
    private void HandleAttackStarted(Vector2 direction)
    {
        motor.PlayAttack(direction, damageDealer.Timeline, false);
    }

    /// <summary>
    /// Stops showing an attack the damage dealer cancelled.
    /// </summary>
    private void HandleAttackCancelled()
    {
        motor.StopAttack();
    }

    /// <summary>
    /// Fires the inspector's attack-range-entered event; called when <see cref="AttackRangeState"/> is entered.
    /// </summary>
    public void InvokeAttackRangeEntered()
    {
        onAttackRangeEntered?.Invoke();
    }

    /// <summary>
    /// Whether the current target is within <see cref="MobConfig.AttackStopDistance"/>, measured collider to collider
    /// (center to center when either has no collider). False without a target.
    /// </summary>
    public bool IsTargetInAttackRange()
    {
        return TryGetAttackDistanceToTarget(out float distance) && distance <= Config.AttackStopDistance;
    }

    /// <summary>
    /// Whether the mob should leave attack range: it has no target, or the target is farther than
    /// <see cref="MobConfig.AttackStopDistance"/> plus <see cref="MobConfig.AttackExitBuffer"/>, which keeps the range
    /// check from flickering at its edge.
    /// </summary>
    public bool ShouldExitAttackRange()
    {
        return !TryGetAttackDistanceToTarget(out float distance) || distance > Config.AttackStopDistance + Config.AttackExitBuffer;
    }

    /// <summary>
    /// One-time setup, run again after <see cref="Rebuild"/>: caches the components, builds an area grid if needed and
    /// prewarms its regions for the movement profile, initializes the components and separation, and registers the
    /// states. Disables the brain when no config is assigned.
    /// </summary>
    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        CacheComponents();

        if (config == null)
        {
            Debug.LogError($"{name} has no {nameof(MobConfig)} assigned; disabling its brain.", this);
            enabled = false;
            return;
        }

        if (navigation != null)
        {
            if (navigation is NavigationGrid2D grid && !grid.IsBuilt)
            {
                grid.BuildGrid();
            }

            navigation.PrewarmRegions(config.MovementProfile);
        }

        motor.Initialize(config);
        pathAgent.Initialize(navigation, motor, config);
        perception.Initialize(players, config);
        patrol.Initialize(navigation, config, random);
        damageDealer.Initialize(config);
        separation = new MobSeparation2D(selfCollider, config, navigation);

        RegisterStates();
        initialized = true;
    }

    /// <summary>
    /// Fetches each sibling component not cached yet; <see cref="StatusEffects"/> is optional and may stay null.
    /// </summary>
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

        if (statuses == null)
        {
            statuses = GetComponent<StatusEffects>();
        }
    }

    /// <summary>
    /// Creates a fresh instance of each of the six states, keyed by <see cref="MobStateId"/>.
    /// </summary>
    private void RegisterStates()
    {
        states.Clear();
        states[MobStateId.Idle] = new IdleState(this);
        states[MobStateId.Patrol] = new PatrolRoamState(this);
        states[MobStateId.Chase] = new ChaseState(this);
        states[MobStateId.AttackRange] = new AttackRangeState(this);
        states[MobStateId.Return] = new ReturnToSpawnState(this);
        states[MobStateId.Search] = new SearchState(this);
    }

    /// <summary>
    /// The gap between the mob and its current target: collider to collider (0 when overlapping) when both have
    /// colliders, else center to center. False without a config, perception or target.
    /// </summary>
    private bool TryGetAttackDistanceToTarget(out float distance)
    {
        distance = float.PositiveInfinity;

        if (config == null || perception == null || perception.CurrentTarget == null)
        {
            return false;
        }

        Transform target = perception.CurrentTarget;
        if (target != cachedTarget)
        {
            cachedTarget = target;
            target.TryGetComponent(out cachedTargetCollider);
        }

        if (selfCollider != null && cachedTargetCollider != null)
        {
            ColliderDistance2D colliderDistance = selfCollider.Distance(cachedTargetCollider);
            distance = Mathf.Max(0f, colliderDistance.distance);
            return true;
        }

        distance = Vector2.Distance(transform.position, target.position);
        return true;
    }

    /// <summary>
    /// Draws a marker in the current state's color, lines to the current target and to the spawn point, and in the
    /// editor a label naming the state.
    /// </summary>
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

    /// <summary>
    /// The debug gizmo color for <paramref name="stateId"/>, white for an unknown id.
    /// </summary>
    private Color GetStateColor(MobStateId stateId)
    {
        return stateId switch
        {
            MobStateId.Idle => idleColor,
            MobStateId.Patrol => patrolColor,
            MobStateId.Chase => chaseColor,
            MobStateId.AttackRange => attackColor,
            MobStateId.Return => returnColor,
            MobStateId.Search => searchColor,
            _ => Color.white
        };
    }
}
