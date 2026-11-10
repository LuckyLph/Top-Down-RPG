using System;
using UnityEngine;
using VContainer;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Wires a player's command source, <see cref="PlayerOrders"/>, <see cref="PlayerMotor2D"/>, weapon and
/// <see cref="PlayerAbilities"/>: each frame it reads a command, ticks the orders (which start casts) and applies
/// their motor, facing and swing requests. The weapon's attack hears of the orders the player gives (its cancel
/// rules), ends on a stun, a cast, a teleport or death, and moves on after the swing; the motor stands still while an
/// attack runs. Feeds the player's status crowd control into the orders and its slow, stun or root into the motor's
/// speed scale. Remote copies only show replicated movement. Draws gizmos for the ability side of its orders: the target of a CastWhenInRange approach and a
/// buffered cast.
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
    private IClock clock;
    private PlayerOrders orders;
    private StatusEffects statuses;
    private bool simulatesMovement = true;

    /// <summary>
    /// Raised with the position after every <see cref="Teleport"/>, so the owner's network transform can snap there on
    /// the other machines.
    /// </summary>
    public event Action<Vector2> Teleported;

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

    /// <summary>
    /// Finds the weapon controller, fails loudly when the control settings are missing and builds the orders.
    /// </summary>
    private void Awake()
    {
        ResolveWeaponController();
        if (controlSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(PlayerControlSettings)}; it cannot take orders.", this);
        }

        EnsureInitialized();
    }

    /// <summary>
    /// Fills empty weapon, motor and ability references from components on the same object.
    /// </summary>
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

    /// <summary>
    /// Logs an error when no clock was injected, since orders cannot run without one, or when a player that simulates
    /// its own movement was never given a command source.
    /// </summary>
    private void Start()
    {
        if (clock == null)
        {
            Debug.LogError($"{name}'s {nameof(PlayerController)} was not injected with an {nameof(IClock)}; its orders cannot run.", this);
        }

        if (simulatesMovement && commandSource == null)
        {
            Debug.LogError($"{name} has no {nameof(IPlayerCommandSource)}; it will not respond to input.", this);
        }
    }

    /// <summary>
    /// Drops every order and stops the motor so a re-enabled player starts idle.
    /// </summary>
    private void OnDisable()
    {
        ClearOrders();
    }

    /// <summary>
    /// Runs one <see cref="Tick"/> per frame.
    /// </summary>
    private void Update()
    {
        Tick();
    }

    /// <summary>
    /// Takes the game clock that times orders and casts.
    /// </summary>
    [Inject]
    public void Construct(IClock gameClock)
    {
        clock = gameClock;
    }

    /// <summary>
    /// Sets where commands come from; null leaves the player without intent (remote copies).
    /// </summary>
    public void SetCommandSource(IPlayerCommandSource source)
    {
        commandSource = source;
    }

    /// <summary>
    /// Whether this machine runs the player's orders and physics (the owner) or only shows replicated movement;
    /// turning it off also drops every order.
    /// </summary>
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

    /// <summary>
    /// Drops every order and moves the body to <paramref name="position"/> at rest (spawns, respawns and arriving at
    /// an entry), then raises <see cref="Teleported"/>.
    /// </summary>
    public void Teleport(Vector2 position)
    {
        ClearOrders();
        Motor.Teleport(position);
        Teleported?.Invoke(position);
    }

    /// <summary>
    /// Turns the player toward <paramref name="direction"/>; a near-zero direction keeps the current facing.
    /// </summary>
    public void Face(Vector2 direction)
    {
        Motor.Face(direction);
    }

    /// <summary>
    /// Drops the current order, ends any attack and stops moving.
    /// </summary>
    public void ClearOrders()
    {
        if (orders != null)
        {
            orders.Clear();
        }

        if (weaponController != null)
        {
            weaponController.CancelAttack();
        }

        Motor.Stop();
    }

    /// <summary>
    /// One frame of control: reads a command, ticks the orders with the player's state and crowd control, applies the
    /// result and scales the motor speed by the status movement scale, or holds it still while an attack runs. Does
    /// nothing on a remote copy.
    /// </summary>
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
        PlayerOrderContext context = new(
            playerMotor.Position,
            clock.Time,
            playerMotor.ReachedDestination,
            playerMotor.StalledTime,
            AttackRange(),
            controls,
            AttackSightRadius());
        Apply(orders.Tick(command, context), controls);
        bool attacking = weaponController != null && weaponController.IsAttacking;
        playerMotor.SetSpeedScale(attacking ? 0f : statuses != null ? statuses.MovementScale : 1f);
    }

    /// <summary>
    /// Forwards an order tick's requests: path to or stop the motor and set or release the aim; then tells the weapon's
    /// attack of the order given (a new order, or the orders asking to move), ends it on a stun or a cast (one running,
    /// or one that started on the tick, even an instant one already over), swings at the order's target, and moves the
    /// attack on.
    /// </summary>
    private void Apply(in PlayerOrderOutput output, StatusControls controls)
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
        if (weaponController == null)
        {
            return;
        }

        if ((controls & StatusControls.Stun) != 0 || orders.Current == PlayerOrderKind.Casting || output.CastStarted)
        {
            weaponController.CancelAttack();
        }
        else if (output.Issued != PlayerOrderIssue.None || output.Motor == PlayerMotorRequest.MoveTo)
        {
            weaponController.Order(output.Issued == PlayerOrderIssue.Move);
        }

        if (output.Swing)
        {
            weaponController.TryAttack(output.AimDirection, orders.AttackTarget);
        }

        weaponController.Tick();
    }

    /// <summary>
    /// The equipped weapon's attack range, or 0 without a weapon.
    /// </summary>
    private float AttackRange()
    {
        return weaponController != null && weaponController.CurrentWeapon != null ? weaponController.CurrentWeapon.AttackRange : 0f;
    }

    /// <summary>
    /// The radius the equipped weapon's attack sweeps toward its target: its projectile's for a ranged weapon, 0 (a
    /// line) for a melee one or without a weapon.
    /// </summary>
    private float AttackSightRadius()
    {
        PlayerWeapon weapon = weaponController != null ? weaponController.CurrentWeapon : null;
        return weapon != null && weapon.FiresProjectile ? weapon.Projectile.Radius : 0f;
    }

    /// <summary>
    /// Builds the orders on first use (Awake, or a test tick before it): configures the motor, finds the abilities
    /// and statuses, and creates <see cref="PlayerOrders"/> over this body's collider and the settings' enemy and
    /// obstacle layers.
    /// Waits while settings are missing.
    /// </summary>
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
        orders = new PlayerOrders(controlSettings, new PlayerUnitQueries(GetComponent<Collider2D>(), controlSettings.EnemyLayers, controlSettings.ObstacleLayers), caster);
    }

    /// <summary>
    /// Draws a line and box to the target of a CastWhenInRange approach and, in the editor, labels the approaching
    /// ability and any buffered cast.
    /// </summary>
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
            Handles.Label(target.transform.position + Vector3.up * 0.6f, $"{approachingSlot + 1} {abilityName}: approaching");
        }

        if (orders.HasBufferedCast)
        {
            AbilityDefinition buffered = abilities.GetAbility(orders.BufferedSlot);
            string abilityName = buffered != null ? buffered.DisplayName : string.Empty;
            Handles.Label(origin + Vector3.down * 0.4f, $"buffered: {orders.BufferedSlot + 1} {abilityName}");
        }
#endif
    }

    /// <summary>
    /// Falls back to the <see cref="PlayerWeaponController"/> on this object when none is assigned.
    /// </summary>
    private void ResolveWeaponController()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }
    }
}
