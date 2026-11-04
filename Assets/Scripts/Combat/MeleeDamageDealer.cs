using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

/// <summary>
/// A mob's melee attack on a timeline (Docs/Animation.md, Phase 7), its <see cref="AttackState"/> on the game clock: a
/// windup, its telegraph, toward the direction locked at the start; a contact, when a <see cref="Hit"/> built from its
/// <see cref="MobConfig"/> lands through <see cref="AttackService"/> on every unit then in the attack's area (a box
/// from the edge of the mob's collider along that direction, under the faction rules); and a recovery. The cooldown,
/// the attack interval, runs from the start. A stun or death cancels a running attack, and one cancelled before
/// contact lands nothing and gives its cooldown back. The brain starts attacks and ticks it where the AI runs (the host,
/// or offline); it raises <see cref="AttackStarted"/>, <see cref="AttackContacted"/> and <see cref="AttackCancelled"/>
/// for the mob's visuals and its network sync. Allocates nothing once built.
/// </summary>
[DisallowMultipleComponent]
public class MeleeDamageDealer : MonoBehaviour
{
    private const int OnlyAttack = 0;

    [SerializeField, Min(0)] private int damageAmount = 1;
    [SerializeField, Min(0f)] private float attackInterval = 0.75f;

    private IClock clock;
    private AttackService attackService;
    private DamageType damageType = DamageType.Physical;
    private StatusEffectDefinition[] statuses = Array.Empty<StatusEffectDefinition>();
    private AttackTimeline timeline = new(0.45f, 0.05f, 0.25f);
    private Vector2 area = new(1f, 0.75f);
    private AttackState attack;
    private Collider2D body;
    private Health health;

    /// <summary>
    /// Raised when an attack starts, with the direction it is locked to.
    /// </summary>
    public event Action<Vector2> AttackStarted;

    /// <summary>
    /// Raised at contact, as the attack's hit resolves.
    /// </summary>
    public event Action AttackContacted;

    /// <summary>
    /// Raised when a running attack ends before its timeline does: a stun, death or the brain cancelling it.
    /// </summary>
    public event Action AttackCancelled;

    public int DamageAmount => damageAmount;
    public DamageType DamageType => damageType;
    public float AttackInterval => attackInterval;
    public AttackTimeline Timeline => timeline;
    public Vector2 Area => area;
    public bool IsAttacking => State.IsBusy;
    public AttackPhase Phase => State.Phase;
    public Vector2 AttackDirection => State.Direction;
    public float ReadyTime => State.ReadyTime;

    /// <summary>
    /// The attack state on the game clock, built on first use.
    /// </summary>
    internal AttackState State => attack ??= new AttackState(clock);

    /// <summary>
    /// The clock the attack runs on, for tests.
    /// </summary>
    internal IClock Clock => clock;

    /// <summary>
    /// Finds the mob's body collider, which its attack's area starts from, and its health.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Fails loudly when no clock was injected: attacks cannot run without one.
    /// </summary>
    private void Start()
    {
        if (clock == null)
        {
            Debug.LogError($"{name}'s {nameof(MeleeDamageDealer)} was not injected with an {nameof(IClock)}; its attacks cannot run.", this);
        }
    }

    /// <summary>
    /// Cancels the attack when the mob dies.
    /// </summary>
    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += HandleDied;
        }
    }

    /// <summary>
    /// Stops listening for death and cancels a running attack.
    /// </summary>
    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
        }

        CancelAttack();
    }

    /// <summary>
    /// Injects the clock attacks run on and the service their hits resolve through.
    /// </summary>
    [Inject]
    public void Construct(IClock gameClock, AttackService attacks)
    {
        clock = gameClock;
        attackService = attacks;
        attack = null;
    }

    /// <summary>
    /// Copies the attack damage, interval, timeline, area, damage type and statuses from <paramref name="config"/>;
    /// keeps the current values when it is null.
    /// </summary>
    public void Initialize(MobConfig config)
    {
        ResolveReferences();
        if (config == null)
        {
            return;
        }

        damageAmount = Mathf.Max(0, config.AttackDamage);
        attackInterval = Mathf.Max(0f, config.AttackInterval);
        timeline = config.AttackTimeline;
        area = config.AttackArea;
        damageType = config.AttackDamageType;
        IReadOnlyList<StatusEffectDefinition> attackStatuses = config.AttackStatuses;
        statuses = new StatusEffectDefinition[attackStatuses.Count];
        for (int i = 0; i < statuses.Length; i++)
        {
            statuses[i] = attackStatuses[i];
        }
    }

    /// <summary>
    /// Starts an attack on the unit <paramref name="target"/> belongs to, its direction locked toward it, when the
    /// cooldown allows and no attack runs, raising <see cref="AttackStarted"/>. False when there is no target or
    /// damage, the attack is not ready, or the target is not a unit; logs an error without an
    /// <see cref="AttackService"/>.
    /// </summary>
    public bool TryStartAttack(Transform target)
    {
        AttackState state = State;
        if (target == null || damageAmount <= 0 || !state.CanStart)
        {
            return false;
        }

        if (attackService == null)
        {
            Debug.LogError($"{nameof(MeleeDamageDealer)} was not injected with an {nameof(AttackService)}.", this);
            return false;
        }

        if (DamageReceiver.FindFor(target) == null)
        {
            return false;
        }

        Vector2 toTarget = target.position - transform.position;
        Vector2 direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.down;
        state.TryStart(OnlyAttack, direction, timeline, attackInterval, false, false);
        AttackStarted?.Invoke(direction);
        return true;
    }

    /// <summary>
    /// Moves the running attack on to the clock's time; at contact its hit lands in its area and
    /// <see cref="AttackContacted"/> is raised. A dead mob's attack is cancelled instead.
    /// </summary>
    public void Tick()
    {
        if (attack == null)
        {
            return;
        }

        if (health != null && health.IsDead)
        {
            CancelAttack();
            return;
        }

        if ((attack.Advance() & AttackStep.Contact) != 0)
        {
            Land(attack.Direction);
            AttackContacted?.Invoke();
        }
    }

    /// <summary>
    /// Ends the running attack at once (a stun, death), giving its cooldown back when its hit has not landed, and
    /// raises <see cref="AttackCancelled"/>.
    /// </summary>
    public void CancelAttack()
    {
        if (attack != null && attack.Cancel() != AttackPhase.None)
        {
            AttackCancelled?.Invoke();
        }
    }

    /// <summary>
    /// The middle of the attack's area toward <paramref name="direction"/>: from the middle of the mob's collider (its
    /// position without one) past the collider's edge along the direction by half the area's depth.
    /// </summary>
    public Vector2 AreaCentre(Vector2 direction)
    {
        Vector2 middle = transform.position;
        float edge = 0f;
        if (body != null && body.enabled)
        {
            Bounds bounds = body.bounds;
            middle = bounds.center;
            edge = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.y) * bounds.extents.y;
        }

        return middle + direction * (edge + area.y * 0.5f);
    }

    /// <summary>
    /// Hits every unit in the attack's area toward <paramref name="direction"/>, this mob as the source.
    /// </summary>
    private void Land(Vector2 direction)
    {
        if (attackService != null)
        {
            attackService.Resolve(transform, new Hit(damageAmount, damageType, statuses), AreaCentre(direction), area, direction);
        }
    }

    /// <summary>
    /// Finds the body collider and the health on this object when not found yet.
    /// </summary>
    private void ResolveReferences()
    {
        if (body == null)
        {
            body = GetComponent<Collider2D>();
        }

        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }

    /// <summary>
    /// Cancels the attack of a mob that just died.
    /// </summary>
    private void HandleDied(Health died)
    {
        CancelAttack();
    }
}
