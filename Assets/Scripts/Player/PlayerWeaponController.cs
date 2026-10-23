using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The player's equipped weapon and its attacks (Docs/Animation.md, Attack timeline and Phase 4). A swing request starts
/// an attack on its <see cref="AttackState"/> when the cooldown and the running attack allow; each <see cref="Tick"/>
/// moves the attack on, and at contact a melee weapon's hit resolves once through <see cref="AttackService"/> around
/// where the player stands, while a ranged weapon launches its projectile through <see cref="ProjectileService"/>
/// toward the attack's target (or along the attack without one). The character's visuals play the weapon's attack clip
/// stretched to the timeline. The player controller passes it the orders the player gives (the weapon's cancel rules),
/// and cancels the attack on stuns, casts, teleports and death; equipping another weapon cancels it too. The owner
/// raises <see cref="AttackStarted"/>, <see cref="AttackContacted"/> and <see cref="AttackCancelled"/> so its attacks can
/// be replicated; other machines replay them on their copy with the weapon the owner attacked with (named by its
/// <see cref="WeaponCatalog"/> index), visual only, and land their contact through <see cref="ResolveRemoteContact"/>,
/// where only the host resolves hits. The weapon only shows in its attack clip; standing and moving the character is
/// drawn unarmed. Equips the starting weapon on <c>Awake</c>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponController : MonoBehaviour
{
    private const int OnlyAttack = 0;

    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerWeapon startingWeapon;
    [SerializeField, Tooltip("Every weapon a player can equip; a weapon is named on the wire by its index here. The starting weapon must be in it.")]
    private WeaponCatalog catalog;
    [SerializeField, Tooltip("The character's visuals, which play the weapon's attacks.")]
    private CharacterVisuals visuals;

    private AttackService attackService;
    private ProjectileService projectileService;
    private IClock clock = UnityClock.Shared;
    private AttackState attack;
    private PlayerWeapon currentWeapon;
    private CharacterAnimationClip attackClip;
    private CharacterAnimationClip[] catalogClips;
    private Health health;
    private float attackSpeed = 1f;
    private UnitTarget attackTarget;

    public event Action<PlayerWeapon> EquippedWeaponChanged;

    /// <summary>
    /// Raised on the machine running the attack when one starts: which attack of the weapon (0, the weapons having one
    /// each so far), its direction and the attack speed it plays at.
    /// </summary>
    public event Action<int, Vector2, float> AttackStarted;

    /// <summary>
    /// Raised on the machine running the attack at contact, as its hit resolves or its projectile leaves: which attack,
    /// the direction it lands toward (a projectile's toward its target) and where the player stands.
    /// </summary>
    public event Action<int, Vector2, Vector2> AttackContacted;

    /// <summary>
    /// Raised on the machine running the attack when one ends before its timeline does: a cancelled windup, a recovery
    /// cut short by an order, a stun, a cast, a teleport, death or another weapon. Not raised when the next attack
    /// chains into it.
    /// </summary>
    public event Action AttackCancelled;

    public PlayerWeapon CurrentWeapon => currentWeapon;
    public PlayerWeapon StartingWeapon => startingWeapon;
    public WeaponCatalog Catalog => catalog;

    /// <summary>
    /// The equipped weapon's index in the catalog, or -1 without one or for a weapon outside it.
    /// </summary>
    public int WeaponIndex => catalog != null ? catalog.IndexOf(currentWeapon) : -1;

    public string CurrentWeaponName
    {
        get
        {
            if (currentWeapon != null)
            {
                return currentWeapon.DisplayName;
            }

            return startingWeapon != null ? startingWeapon.DisplayName : string.Empty;
        }
    }
    public Vector2 CurrentFacingDirection => playerController != null ? playerController.FacingDirection : Vector2.down;
    public AttackPhase AttackPhase => State.Phase;
    public bool IsAttacking => State.IsBusy;
    public float AttackSpeed => attackSpeed;

    /// <summary>
    /// The unit the running attack is aimed at, or none.
    /// </summary>
    public UnitTarget AttackTarget => IsAttacking ? attackTarget : default;

    /// <summary>
    /// The attack state on the game clock, built on first use.
    /// </summary>
    internal AttackState State => attack ??= new AttackState(clock);

    /// <summary>
    /// Finds the player controller, the visuals and the health, equips the starting weapon, and fails loudly when the
    /// catalog lacks it (other machines could not be told about it).
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        health = GetComponent<Health>();
        if (catalog != null && startingWeapon != null && catalog.IndexOf(startingWeapon) < 0)
        {
            Debug.LogError($"{name}'s starting weapon {startingWeapon.name} is not in its {nameof(WeaponCatalog)}.", this);
        }

        Equip(startingWeapon);
    }

    /// <summary>
    /// Fills empty player controller and visuals references from this object.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Ends a running attack, and any attack a copy is playing, when the player is disabled (death).
    /// </summary>
    private void OnDisable()
    {
        CancelAttack();
        if (visuals != null)
        {
            visuals.StopAction(CharacterActionKind.Attack);
        }
    }

    /// <summary>
    /// Takes the services that resolve hits and fly projectiles and the game clock that times attacks; a null clock
    /// falls back to the shared Unity clock.
    /// </summary>
    [Inject]
    public void Construct(AttackService attacks, ProjectileService projectiles, IClock gameClock)
    {
        attackService = attacks;
        projectileService = projectiles;
        clock = gameClock ?? UnityClock.Shared;
        attack = null;
    }

    /// <summary>
    /// Makes <paramref name="weapon"/> (null for none) the current weapon, ending any attack (and any attack a copy is
    /// playing), looks its attack clip up in the character's set and raises <see cref="EquippedWeaponChanged"/>. Logs
    /// an error when the set lacks the clip the weapon names.
    /// </summary>
    public void Equip(PlayerWeapon weapon)
    {
        ResolveReferences();
        CancelAttack();
        if (visuals != null)
        {
            visuals.StopAction(CharacterActionKind.Attack);
        }

        CharacterAnimationClip clip = ClipFor(weapon);
        currentWeapon = weapon;
        attackClip = clip;
        EquippedWeaponChanged?.Invoke(currentWeapon);
    }

    /// <summary>
    /// Equips the catalog's weapon at <paramref name="index"/>; false, changing nothing, when there is none.
    /// </summary>
    public bool EquipFromCatalog(int index)
    {
        PlayerWeapon weapon = catalog != null ? catalog.Get(index) : null;
        if (weapon == null)
        {
            return false;
        }

        Equip(weapon);
        return true;
    }

    /// <summary>
    /// The catalog's weapon at <paramref name="index"/>, or the weapon equipped here for an index outside the catalog
    /// (a weapon the catalog does not list cannot be named on the wire).
    /// </summary>
    public PlayerWeapon WeaponFor(int index)
    {
        PlayerWeapon listed = catalog != null ? catalog.Get(index) : null;
        return listed != null ? listed : currentWeapon;
    }

    /// <summary>
    /// Sets the multiplier the weapon's timeline and cooldown are divided by, from the next attack on (1 by default;
    /// statuses will scale it).
    /// </summary>
    public void SetAttackSpeed(float multiplier)
    {
        attackSpeed = multiplier > 0f ? multiplier : 1f;
    }

    /// <summary>
    /// A swing request toward <paramref name="direction"/> without a target unit.
    /// </summary>
    public bool TryAttack(Vector2 direction)
    {
        return TryAttack(direction, default);
    }

    /// <summary>
    /// A swing request toward <paramref name="direction"/> (the facing direction when it is zero) at
    /// <paramref name="target"/> (none for an attack without one): starts an attack when the cooldown and the running
    /// attack allow, ending a recovery it chains into. Returns whether it started.
    /// </summary>
    public bool TryAttack(Vector2 direction, in UnitTarget target)
    {
        ResolveReferences();
        AttackState state = State;
        if (currentWeapon == null || !state.CanStart)
        {
            return false;
        }

        Vector2 attackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : CurrentFacingDirection;
        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = Vector2.down;
        }

        AttackTimeline timeline = currentWeapon.Timeline.At(attackSpeed);
        state.TryStart(OnlyAttack, attackDirection, timeline, currentWeapon.AttackCooldown / attackSpeed, currentWeapon.WindupCancelledByMove, currentWeapon.RecoveryCancellable);
        attackTarget = target;
        if (visuals != null)
        {
            visuals.PlayAction(attackClip, attackDirection, timeline, false);
        }

        AttackStarted?.Invoke(OnlyAttack, attackDirection, attackSpeed);
        return true;
    }

    /// <summary>
    /// Passes an order the player gave to the running attack, <paramref name="move"/> for a move or Stop, under the
    /// weapon's cancel rules; an attack it ends stops showing and raises <see cref="AttackCancelled"/>.
    /// </summary>
    public void Order(bool move)
    {
        if (State.Order(move))
        {
            EndEarly();
        }
    }

    /// <summary>
    /// Ends the running attack at once (a stun, a cast, a teleport, death, another weapon), giving its cooldown back
    /// when its hit has not landed.
    /// </summary>
    public void CancelAttack()
    {
        if (attack != null && attack.Cancel() != AttackPhase.None)
        {
            EndEarly();
        }
    }

    /// <summary>
    /// Moves the running attack on to the clock's time: at contact its hit lands around the player (a melee area, or a
    /// projectile leaving toward the target) and <see cref="AttackContacted"/> is raised; a recovery an earlier order
    /// cut short stops showing.
    /// </summary>
    public void Tick()
    {
        if (attack == null)
        {
            return;
        }

        AttackStep step = attack.Advance();
        if ((step & AttackStep.Contact) != 0)
        {
            Vector2 position = transform.position;
            Vector2 direction = ContactDirection(currentWeapon, attack.Direction, position, attackTarget);
            Land(currentWeapon, direction, position, attackTarget);
            AttackContacted?.Invoke(attack.Attack, direction, position);
        }

        if ((step & AttackStep.CutShort) != 0)
        {
            EndEarly();
        }
    }

    /// <summary>
    /// Plays an attack the owner started on another machine on this copy: the clip of the catalog's weapon at
    /// <paramref name="weaponIndex"/> (this copy's weapon outside the catalog) toward <paramref name="direction"/>
    /// (down when zero) at <paramref name="speed"/>, its windup holding until the owner's contact arrives. Visual only:
    /// no cooldown, hit or event.
    /// </summary>
    public void PlayRemoteAttack(int weaponIndex, int attackIndex, Vector2 direction, float speed)
    {
        PlayerWeapon weapon = WeaponFor(weaponIndex);
        if (weapon == null || visuals == null || attackIndex != OnlyAttack)
        {
            return;
        }

        Vector2 attackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        visuals.PlayAction(ClipFor(weapon), attackDirection, weapon.Timeline.At(speed), true);
    }

    /// <summary>
    /// Plays the contact of the owner's attack on this copy, from now.
    /// </summary>
    public void PlayRemoteContact()
    {
        if (visuals != null)
        {
            visuals.ReachContact();
        }
    }

    /// <summary>
    /// Stops the owner's attack on this copy, which ended early on its machine.
    /// </summary>
    public void StopRemoteAttack()
    {
        if (visuals != null)
        {
            visuals.StopAction(CharacterActionKind.Attack);
        }
    }

    /// <summary>
    /// On every machine but the owner, lands the contact of an attack the owner made with the catalog's weapon at
    /// <paramref name="weaponIndex"/> toward <paramref name="direction"/> while standing at
    /// <paramref name="attackerPosition"/>, rather than around this lagging copy: a melee hit resolves there on the
    /// host only, and a projectile leaves there toward <paramref name="target"/> on every machine, hitting only on the
    /// host. Ignored while this machine sees the player dead.
    /// </summary>
    public void ResolveRemoteContact(int weaponIndex, int attackIndex, Vector2 direction, Vector2 attackerPosition, in UnitTarget target)
    {
        PlayerWeapon weapon = WeaponFor(weaponIndex);
        if (weapon == null || attackIndex != OnlyAttack || (health != null && health.IsDead))
        {
            return;
        }

        Land(weapon, direction, attackerPosition, target);
    }

    /// <summary>
    /// The direction an attack of <paramref name="weapon"/> lands toward: a projectile's toward the middle of a living
    /// target's collider from <paramref name="position"/>, else <paramref name="attackDirection"/>.
    /// </summary>
    private static Vector2 ContactDirection(PlayerWeapon weapon, Vector2 attackDirection, Vector2 position, in UnitTarget target)
    {
        if (weapon == null || !weapon.FiresProjectile || target.Health == null || target.Health.IsDead || target.Collider == null)
        {
            return attackDirection;
        }

        Vector2 toTarget = (Vector2)target.Collider.bounds.center - position;
        return toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : attackDirection;
    }

    /// <summary>
    /// Lands an attack of <paramref name="weapon"/> made from <paramref name="position"/> toward
    /// <paramref name="direction"/>: a melee weapon's hit area is searched (on the authority only), a ranged weapon's
    /// projectile leaves toward <paramref name="target"/>.
    /// </summary>
    private void Land(PlayerWeapon weapon, Vector2 direction, Vector2 position, in UnitTarget target)
    {
        if (weapon == null)
        {
            return;
        }

        if (weapon.FiresProjectile)
        {
            if (projectileService != null)
            {
                projectileService.Launch(gameObject, weapon.Projectile, weapon.Hit, position, direction, target);
            }

            return;
        }

        if (attackService != null)
        {
            attackService.Resolve(transform, weapon, direction, position);
        }
    }

    /// <summary>
    /// Stops showing an attack that ended early and raises <see cref="AttackCancelled"/>.
    /// </summary>
    private void EndEarly()
    {
        if (visuals != null)
        {
            visuals.StopAction(CharacterActionKind.Attack);
        }

        AttackCancelled?.Invoke();
    }

    /// <summary>
    /// The attack clip of <paramref name="weapon"/> in the character's set: the equipped weapon's, a catalog weapon's
    /// looked up once and kept, or another's looked up by name.
    /// </summary>
    private CharacterAnimationClip ClipFor(PlayerWeapon weapon)
    {
        if (weapon == null)
        {
            return null;
        }

        if (weapon == currentWeapon && attackClip != null)
        {
            return attackClip;
        }

        int index = catalog != null ? catalog.IndexOf(weapon) : -1;
        if (index < 0)
        {
            return FindClip(weapon.AttackClip);
        }

        if (catalogClips == null || catalogClips.Length != catalog.Count)
        {
            catalogClips = new CharacterAnimationClip[catalog.Count];
        }

        if (catalogClips[index] == null)
        {
            catalogClips[index] = FindClip(weapon.AttackClip);
        }

        return catalogClips[index];
    }

    /// <summary>
    /// The clip of the character's set named <paramref name="clipName"/>, or null without visuals or a name; logs an
    /// error when the set has no such clip.
    /// </summary>
    private CharacterAnimationClip FindClip(string clipName)
    {
        if (visuals == null || visuals.AnimationSet == null || string.IsNullOrEmpty(clipName))
        {
            return null;
        }

        CharacterAnimationClip clip = visuals.AnimationSet.Find(clipName);
        if (clip == null)
        {
            Debug.LogError($"{name}'s {nameof(CharacterAnimationSet)} has no clip '{clipName}' for its weapon.", this);
        }

        return clip;
    }

    /// <summary>
    /// Test setup without the prefab: sets the catalog the weapons are named by.
    /// </summary>
    internal void SetCatalog(WeaponCatalog weaponCatalog)
    {
        catalog = weaponCatalog;
        catalogClips = null;
    }

    /// <summary>
    /// Falls back to the <see cref="PlayerController"/> on this object and the <see cref="CharacterVisuals"/> under it
    /// when none is assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (visuals == null)
        {
            visuals = GetComponentInChildren<CharacterVisuals>(true);
        }
    }
}
