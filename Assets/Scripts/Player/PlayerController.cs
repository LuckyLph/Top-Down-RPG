using UnityEngine;
using VContainer;

/// <summary>
/// Wires a player's command source, <see cref="PlayerOrders"/>, <see cref="PlayerMotor2D"/> and weapon: each frame
/// it reads a command, ticks the orders and applies their motor, facing and swing requests. Remote copies only show
/// replicated movement.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerMotor2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private PlayerControlSettings controlSettings;
    [SerializeField] private PlayerWeaponController weaponController;
    [SerializeField] private PlayerMotor2D motor;

    private IPlayerCommandSource commandSource;
    private IClock clock = UnityClock.Shared;
    private PlayerOrders orders;
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
        PlayerOrderContext context = new(
            playerMotor.Position,
            clock.Time,
            playerMotor.ReachedDestination,
            playerMotor.StalledTime,
            AttackRange());
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

        if (output.HasAim)
        {
            playerMotor.Face(output.AimDirection);
        }

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
        orders = new PlayerOrders(controlSettings, new PlayerUnitQueries(GetComponent<Collider2D>()));
    }

    private void ResolveWeaponController()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }
    }
}
