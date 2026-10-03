using UnityEngine;
using VContainer;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Wires a player's command source, <see cref="PlayerOrders"/>, <see cref="PlayerMotor2D"/>, weapon and
/// <see cref="PlayerAbilities"/>: each frame it reads a command, ticks the orders (which start casts) and applies
/// their motor, facing and swing requests. Feeds the player's status crowd control into the orders and its slow,
/// stun or root into the motor's speed scale. Remote copies only show replicated movement. Draws gizmos for the ability side of its orders: the target of a CastWhenInRange approach
/// and a buffered cast.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerMotor2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private PlayerControlSettings controlSettings;
    [SerializeField] private PlayerWeaponController weaponController;
    [SerializeField] private PlayerMotor2D motor;
    [SerializeField] private PlayerAbilities abilities;

    [Header("Debug")]
    [SerializeField, Tooltip("Draw the target a unit cast is walking into range of, and any buffered cast.")]
    private bool drawAbilityOrderGizmos = true;
    [SerializeField] private Color approachGizmoColor = new(1f, 1f, 1f, 0.8f);

    private IPlayerCommandSource commandSource;
    private IClock clock = UnityClock.Shared;
    private PlayerOrders orders;
    private StatusEffects statuses;
    private bool simulatesMovement = true;

    public PlayerControlSettings ControlSettings => controlSettings;
    public Vector2 FacingDirection => Motor.FacingDirection;
    public Vector2 CurrentMove => Motor.CurrentMove;
    public bool SimulatesMovement => simulatesMovement;
    public PlayerOrderKind CurrentOrder => orders != null ? orders.Current : PlayerOrderKind.Idle;

    internal PlayerOrders Orders => orders;

    private PlayerMotor2D Motor
    {
        get
        {
            if (motor == null)
            {
                motor = GetComponent<PlayerMotor2D>();
            }

            return motor;
        }
    }

    private void Awake()
    {
        ResolveWeaponController();
        if (controlSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(PlayerControlSettings)}; it cannot take orders.", this);
        }

        EnsureInitialized();
    }

    private void OnValidate()
    {
        ResolveWeaponController();
        if (motor == null)
        {
            motor = GetComponent<PlayerMotor2D>();
        }

        if (abilities == null)
        {
            abilities = GetComponent<PlayerAbilities>();
        }
    }

    private void Start()
    {
        if (simulatesMovement && commandSource == null)
        {
            Debug.LogError($"{name} has no {nameof(IPlayerCommandSource)}; it will not respond to input.", this);
        }
    }

    private void OnDisable()
    {
        ClearOrders();
    }

    private void Update()
    {
        Tick();
    }

    [Inject]
    public void Construct(IClock gameClock)
    {
        clock = gameClock ?? UnityClock.Shared;
    }

    public void SetCommandSource(IPlayerCommandSource source)
    {
        commandSource = source;
    }

    public void SetSimulatesMovement(bool simulates)
    {
        simulatesMovement = simulates;
        Motor.SetSimulatesMovement(simulates);
        if (!simulates)
        {
            ClearOrders();
        }
    }

    /// <summary>
    /// Shows another machine's movement on this remote copy.
    /// </summary>
    public void ShowRemoteMovement(Vector2 move, Vector2 facing)
    {
        Motor.ShowRemoteMovement(move, facing);
    }

    public void Teleport(Vector2 position)
    {
        ClearOrders();
        Motor.Teleport(position);
    }

    public void Face(Vector2 direction)
    {
        Motor.Face(direction);
    }

    /// <summary>
    /// Drops the current order and stops moving.
    /// </summary>
    public void ClearOrders()
    {
        if (orders != null)
        {
            orders.Clear();
        }

        Motor.Stop();
    }

    internal void Tick()
    {
        EnsureInitialized();
        if (!simulatesMovement || orders == null)
        {
            return;
        }

        PlayerCommand command = commandSource != null ? commandSource.ReadCommand() : default;
        PlayerMotor2D playerMotor = Motor;
        StatusControls controls = statuses != null ? statuses.Controls : StatusControls.None;
        playerMotor.SetSpeedScale(statuses != null ? statuses.MovementScale : 1f);
        PlayerOrderContext context = new(
            playerMotor.Position,
            clock.Time,
            playerMotor.ReachedDestination,
            playerMotor.StalledTime,
            AttackRange(),
            controls);
        Apply(orders.Tick(command, context));
    }

    private void Apply(in PlayerOrderOutput output)
    {
        PlayerMotor2D playerMotor = Motor;
        if (output.Motor == PlayerMotorRequest.MoveTo)
        {
            playerMotor.MoveTo(output.Destination);
        }
        else if (output.Motor == PlayerMotorRequest.Stop)
        {
            playerMotor.Stop();
        }

        playerMotor.SetAim(output.HasAim, output.AimDirection);

        if (output.Swing && weaponController != null)
        {
            weaponController.TryAttack(output.AimDirection);
        }
    }

    private float AttackRange()
    {
        return weaponController != null && weaponController.CurrentWeapon != null ? weaponController.CurrentWeapon.AttackRange : 0f;
    }

    private void EnsureInitialized()
    {
        if (orders != null || controlSettings == null)
        {
            return;
        }

        Motor.Initialize(controlSettings);
        if (abilities == null)
        {
            abilities = GetComponent<PlayerAbilities>();
        }

        statuses = GetComponent<StatusEffects>();
        IAbilityCaster caster = abilities != null ? abilities : null;
        orders = new PlayerOrders(controlSettings, new PlayerUnitQueries(GetComponent<Collider2D>()), caster);
    }

    private void OnDrawGizmos()
    {
        if (!drawAbilityOrderGizmos || orders == null || abilities == null)
        {
            return;
        }

        Vector3 origin = transform.position;
        int approachingSlot = orders.ApproachingSlot;
        Health target = orders.AttackTarget.Health;
        if (approachingSlot >= 0 && target != null)
        {
            Gizmos.color = approachGizmoColor;
            Gizmos.DrawLine(origin, target.transform.position);
            Gizmos.DrawWireCube(target.transform.position, new Vector3(0.5f, 0.5f, 0f));
        }

#if UNITY_EDITOR
        Handles.color = approachGizmoColor;
        if (approachingSlot >= 0 && target != null)
        {
            AbilityDefinition approaching = abilities.GetAbility(approachingSlot);
            string abilityName = approaching != null ? approaching.DisplayName : string.Empty;
            Handles.Label(target.transform.position + Vector3.up * 0.6f, $"{AbilitySlots.KeyName(approachingSlot)} {abilityName}: approaching");
        }

        if (orders.HasBufferedCast)
        {
            AbilityDefinition buffered = abilities.GetAbility(orders.BufferedSlot);
            string abilityName = buffered != null ? buffered.DisplayName : string.Empty;
            Handles.Label(origin + Vector3.down * 0.4f, $"buffered: {AbilitySlots.KeyName(orders.BufferedSlot)} {abilityName}");
        }
#endif
    }

    private void ResolveWeaponController()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }
    }
}
