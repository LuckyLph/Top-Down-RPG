using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The player's equipped weapon and its attacks (Docs/Animation.md, Attack timeline and Phase 4). A swing request starts
/// an attack on its <see cref="AttackState"/> when the cooldown and the running attack allow; each <see cref="Tick"/>
/// moves the attack on, and at contact a melee weapon's hit resolves once through <see cref="AttackService"/> around
/// where the player stands, while a ranged weapon launches its projectile through <see cref="ProjectileService"/>
/// toward the attack's target (or along the attack without one). The character's visuals play the weapon's attack clip
/// stretched to the timeline. A weapon with several attacks starts with its first after being equipped, then follows
/// each attack with one of the others at random, never the same twice in a row (Docs/PlayerCharacter.md, Phase 4). The
/// player controller passes it the orders the player gives (the weapon's cancel rules), and cancels the attack on stuns,
/// casts, teleports and death; equipping another weapon cancels it too. The owner raises <see cref="AttackStarted"/>,
/// <see cref="AttackContacted"/> and <see cref="AttackCancelled"/> so its attacks can be replicated; other machines
/// replay them on their copy with the weapon and the attack the owner made (the weapon named by its
/// <see cref="WeaponCatalog"/> index), visual only, and land their contact through <see cref="ResolveRemoteContact"/>,
/// where only the host resolves hits. The weapon only shows in its attack clips; standing and moving the character is
/// drawn unarmed. Equips the starting weapon on <c>Awake</c>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponController : MonoBehaviour
{
    private const int FirstAttack = 0;

    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerWeapon startingWeapon;
    [SerializeField, Tooltip("Every weapon a player can equip; a weapon is named on the wire by its index here. The starting weapon must be in it.")]
    private WeaponCatalog catalog;
    [SerializeField, Tooltip("The character's visuals, which play the weapon's attacks.")]
    private CharacterVisuals visuals;

    private AttackService attackService;
    private ProjectileService projectileService;
    private IClock clock;
    private IRandom random;
    private AttackState attack;
    private PlayerWeapon currentWeapon;
    private int lastAttack = -1;
    private CharacterAnimationClip[] attackClips;
    private CharacterAnimationClip[][] catalogClips;
    private CharacterAnimationSet clipsFrom;
    private Health health;
    private float attackSpeed = 1f;
    private UnitTarget attackTarget;

    public event Action<PlayerWeapon> EquippedWeaponChanged;

    /// <summary>
    /// Raised on the machine running the attack when one starts: which attack of the weapon (0 its first), its direction
    /// and the attack speed it plays at.
    /// </summary>
    public event Action<int, Vector2, float> AttackStarted;

    /// <summary>
    /// Raised on the machine running the attack at contact, as its hit resolves or its projectile leaves: which attack,
    /// the direction it lands toward (a projectile's toward its target), where the player stands and the unit the
    /// attack was aimed at (none without one), even when the same update also ended the attack.
    /// </summary>
    public event Action<int, Vector2, Vector2, UnitTarget> AttackContacted;

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

    internal string CurrentWeaponName
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
    /// Fails loudly when no clock was injected: attacks cannot run without one.
    /// </summary>
    private void Start()
    {
        if (clock == null)
        {
            Debug.LogError($"{name}'s {nameof(PlayerWeaponController)} was not injected with an {nameof(IClock)}; its attacks cannot run.", this);
        }
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
    /// Takes the services that resolve hits and fly projectiles, the game clock that times attacks and the random source
    /// that picks a weapon's next attack among its others (without one they follow in order).
    /// </summary>
    [Inject]
    public void Construct(AttackService attacks, ProjectileService projectiles, IClock gameClock, IRandom attackRandom)
    {
        attackService = attacks;
        projectileService = projectiles;
        clock = gameClock;
        random = attackRandom;
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

        Switch(weapon);
    }

    /// <summary>
    /// On a copy, makes <paramref name="weapon"/> the current weapon as the owner switched to it, leaving the attack the
    /// copy shows alone: the owner's switch already sent the cancel of the attack it ended, and an attack it started
    /// after the switch, which names its weapon, can arrive before the switch does.
    /// </summary>
    public void EquipOnCopy(PlayerWeapon weapon)
    {
        ResolveReferences();
        Switch(weapon);
    }

    /// <summary>
    /// Equips the catalog's weapon at <paramref name="index"/>; false, changing nothing, when there is none. For tests.
    /// </summary>
    internal bool EquipFromCatalog(int index)
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
    /// A swing request toward <paramref name="direction"/> without a target unit.
    /// </summary>
    public bool TryAttack(Vector2 direction)
    {
        return TryAttack(direction, default);
    }

    /// <summary>
    /// A swing request toward <paramref name="direction"/> (the facing direction when it is zero) at
    /// <paramref name="target"/> (none for an attack without one): starts an attack when the cooldown and the running
    /// attack allow, ending a recovery it chains into: the weapon's next attack (see <see cref="NextAttack"/>). Returns
    /// whether it started.
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

        int attackIndex = NextAttack();
        AttackTimeline timeline = currentWeapon.Timeline.At(attackSpeed);
        state.TryStart(attackIndex, attackDirection, timeline, currentWeapon.AttackCooldown / attackSpeed, currentWeapon.WindupCancelledByMove, currentWeapon.RecoveryCancellable);
        attackTarget = target;
        lastAttack = attackIndex;
        if (visuals != null)
        {
            visuals.PlayAction(ClipFor(currentWeapon, attackIndex), attackDirection, timeline, false);
        }

        AttackStarted?.Invoke(attackIndex, attackDirection, attackSpeed);
        return true;
    }

    /// <summary>
    /// The equipped weapon's attack to start next: its first after it was equipped, then one of the attacks other than
    /// the last at random (the next in order without a random source), so none plays twice in a row.
    /// </summary>
    private int NextAttack()
    {
        int count = currentWeapon.AttackCount;
        if (count <= 1 || lastAttack < 0 || lastAttack >= count)
        {
            return FirstAttack;
        }

        int others = count - 1;
        int skip = random != null && others > 1 ? Mathf.Min((int)random.Range(0f, others), others - 1) : 0;
        return (lastAttack + 1 + skip) % count;
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
    /// projectile leaving toward the target), a client predicts its own melee hit there so its contact frame holds at
    /// once (<see cref="AttackService.Predict"/>), and <see cref="AttackContacted"/> is raised; a recovery an earlier
    /// order cut short stops showing.
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
            if (attackService != null)
            {
                attackService.Predict(transform, currentWeapon, direction, position);
            }

            AttackContacted?.Invoke(attack.Attack, direction, position, attackTarget);
        }

        if ((step & AttackStep.CutShort) != 0)
        {
            EndEarly();
        }
    }

    /// <summary>
    /// Plays an attack the owner started on another machine on this copy: the clip of attack
    /// <paramref name="attackIndex"/> of the catalog's weapon at <paramref name="weaponIndex"/> (this copy's weapon
    /// outside the catalog; nothing for an attack the weapon lacks) toward <paramref name="direction"/>
    /// (down when zero) at <paramref name="speed"/>, its windup holding until the owner's contact arrives, at most the
    /// character's contact wait past the windup, after which the copy goes back to its locomotion (a later contact
    /// still lands its hit). Visual only: no cooldown, hit or event.
    /// </summary>
    public void PlayRemoteAttack(int weaponIndex, int attackIndex, Vector2 direction, float speed)
    {
        PlayerWeapon weapon = WeaponFor(weaponIndex);
        if (weapon == null || visuals == null || attackIndex < 0 || attackIndex >= weapon.AttackCount)
        {
            return;
        }

        Vector2 attackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        visuals.PlayAction(ClipFor(weapon, attackIndex), attackDirection, weapon.Timeline.At(speed), true);
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
    /// host. Every attack of a weapon lands alike. Ignored for an attack the weapon lacks, and while this machine sees the
    /// player dead.
    /// </summary>
    public void ResolveRemoteContact(int weaponIndex, int attackIndex, Vector2 direction, Vector2 attackerPosition, in UnitTarget target)
    {
        PlayerWeapon weapon = WeaponFor(weaponIndex);
        if (weapon == null || attackIndex < 0 || attackIndex >= weapon.AttackCount || (health != null && health.IsDead))
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
    /// Makes <paramref name="weapon"/> the current weapon, looks its attack clips up, starts its attacks over from its
    /// first and raises <see cref="EquippedWeaponChanged"/>.
    /// </summary>
    private void Switch(PlayerWeapon weapon)
    {
        currentWeapon = weapon;
        attackClips = null;
        lastAttack = -1;
        ClipFor(weapon, FirstAttack);
        EquippedWeaponChanged?.Invoke(currentWeapon);
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
    /// The clip of attack <paramref name="attack"/> of <paramref name="weapon"/> in the character's set (the first
    /// attack's for an index outside its attacks): the equipped weapon's clips and a catalog weapon's are looked up once
    /// and kept, another's looked up by name. The clips kept are forgotten once the visuals draw another set (the
    /// player's look changed).
    /// </summary>
    private CharacterAnimationClip ClipFor(PlayerWeapon weapon, int attack)
    {
        if (weapon == null)
        {
            return null;
        }

        CharacterAnimationSet set = visuals != null ? visuals.AnimationSet : null;
        if (!ReferenceEquals(set, clipsFrom))
        {
            clipsFrom = set;
            attackClips = null;
            catalogClips = null;
        }

        CharacterAnimationClip[] clips;
        int index = catalog != null ? catalog.IndexOf(weapon) : -1;
        if (weapon == currentWeapon)
        {
            clips = attackClips ??= ClipsOf(weapon);
        }
        else if (index < 0)
        {
            clips = ClipsOf(weapon);
        }
        else
        {
            if (catalogClips == null || catalogClips.Length != catalog.Count)
            {
                catalogClips = new CharacterAnimationClip[catalog.Count][];
            }

            clips = catalogClips[index] ??= ClipsOf(weapon);
        }

        return attack > FirstAttack && attack < clips.Length ? clips[attack] : clips[FirstAttack];
    }

    /// <summary>
    /// The clip of each of <paramref name="weapon"/>'s attacks in the character's set. The set must hold the first
    /// attack's clip (an error is logged when it does not); one it lacks past the first plays the first's instead, as
    /// the Bandit's set holds only each weapon's first attack.
    /// </summary>
    private CharacterAnimationClip[] ClipsOf(PlayerWeapon weapon)
    {
        CharacterAnimationClip[] clips = new CharacterAnimationClip[weapon.AttackCount];
        clips[FirstAttack] = FindClip(weapon.AttackClip);
        CharacterAnimationSet set = visuals != null ? visuals.AnimationSet : null;
        for (int i = FirstAttack + 1; i < clips.Length; i++)
        {
            string clipName = weapon.AttackClipOf(i);
            CharacterAnimationClip clip = set != null && !string.IsNullOrEmpty(clipName) ? set.Find(clipName) : null;
            clips[i] = clip ?? clips[FirstAttack];
        }

        return clips;
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
