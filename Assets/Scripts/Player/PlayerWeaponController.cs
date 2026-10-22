using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The player's equipped weapon and its attacks (Docs/Animation.md, Attack timeline). A swing request starts an attack
/// on its <see cref="AttackState"/> when the cooldown and the running attack allow; each <see cref="Tick"/> moves the
/// attack on, its hit resolving once at contact through <see cref="AttackService"/> around where the player stands, and
/// the character's visuals play the weapon's attack clip stretched to the timeline. The player controller passes it
/// the orders the player gives (the weapon's cancel rules), and cancels the attack on stuns, casts, teleports and death.
/// The owner raises <see cref="AttackStarted"/>, <see cref="AttackContacted"/> and <see cref="AttackCancelled"/> so its
/// attacks can be replicated; other machines replay them on their copy, visual only, and the host resolves a client's
/// contact through <see cref="ResolveRemoteContact"/>. The weapon only shows in its attack clip; standing and moving the
/// character is drawn unarmed. Equips the starting weapon on <c>Awake</c>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponController : MonoBehaviour
{
    private const int OnlyAttack = 0;

    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerWeapon startingWeapon;
    [SerializeField, Tooltip("The character's visuals, which play the weapon's attacks.")]
    private CharacterVisuals visuals;

    private AttackService attackService;
    private IClock clock = UnityClock.Shared;
    private AttackState attack;
    private PlayerWeapon currentWeapon;
    private CharacterAnimationClip attackClip;
    private Health health;
    private float attackSpeed = 1f;

    public event Action<PlayerWeapon> EquippedWeaponChanged;

    /// <summary>
    /// Raised on the machine running the attack when one starts: which attack of the weapon (0, the weapons having one
    /// each so far), its direction and the attack speed it plays at.
    /// </summary>
    public event Action<int, Vector2, float> AttackStarted;

    /// <summary>
    /// Raised on the machine running the attack at contact, as its hit resolves: which attack, its direction and where
    /// the player stands.
    /// </summary>
    public event Action<int, Vector2, Vector2> AttackContacted;

    /// <summary>
    /// Raised on the machine running the attack when one ends before its timeline does: a cancelled windup, a recovery
    /// cut short by an order, a stun, a cast, a teleport or death. Not raised when the next attack chains into it.
    /// </summary>
    public event Action AttackCancelled;

    public PlayerWeapon CurrentWeapon => currentWeapon;
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
    /// The attack state on the game clock, built on first use.
    /// </summary>
    internal AttackState State => attack ??= new AttackState(clock);

    /// <summary>
    /// Finds the player controller, the visuals and the health, then equips the starting weapon.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        health = GetComponent<Health>();
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
            visuals.StopAction();
        }
    }

    /// <summary>
    /// Takes the service that resolves hits and the game clock that times attacks; null falls back to the shared Unity
    /// clock.
    /// </summary>
    [Inject]
    public void Construct(AttackService attacks, IClock gameClock)
    {
        attackService = attacks;
        clock = gameClock ?? UnityClock.Shared;
        attack = null;
    }

    /// <summary>
    /// Makes <paramref name="weapon"/> (null for none) the current weapon, ending any attack, looks its attack clip up
    /// in the character's set and raises <see cref="EquippedWeaponChanged"/>. Logs an error when the set lacks the clip
    /// the weapon names.
    /// </summary>
    public void Equip(PlayerWeapon weapon)
    {
        ResolveReferences();
        CancelAttack();
        currentWeapon = weapon;
        attackClip = FindClip(weapon != null ? weapon.AttackClip : null);
        EquippedWeaponChanged?.Invoke(currentWeapon);
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
    /// A swing request toward <paramref name="direction"/> (the facing direction when it is zero): starts an attack when
    /// the cooldown and the running attack allow, ending a recovery it chains into. Returns whether it started.
    /// </summary>
    public bool TryAttack(Vector2 direction)
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
    /// Ends the running attack at once (a stun, a cast, a teleport, death), giving its cooldown back when its hit has
    /// not landed.
    /// </summary>
    public void CancelAttack()
    {
        if (attack != null && attack.Cancel() != AttackPhase.None)
        {
            EndEarly();
        }
    }

    /// <summary>
    /// Moves the running attack on to the clock's time: at contact its hit resolves around the player and
    /// <see cref="AttackContacted"/> is raised; a recovery an earlier order cut short stops showing.
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
            if (attackService != null)
            {
                attackService.Resolve(transform, currentWeapon, attack.Direction, position);
            }

            AttackContacted?.Invoke(attack.Attack, attack.Direction, position);
        }

        if ((step & AttackStep.CutShort) != 0)
        {
            EndEarly();
        }
    }

    /// <summary>
    /// Plays an attack the owner started on another machine on this copy: the weapon's clip toward
    /// <paramref name="direction"/> (down when zero) at <paramref name="speed"/>, its windup holding until the owner's
    /// contact arrives. Visual only: no cooldown, hit or event.
    /// </summary>
    public void PlayRemoteAttack(int attackIndex, Vector2 direction, float speed)
    {
        if (currentWeapon == null || visuals == null || attackIndex != OnlyAttack)
        {
            return;
        }

        Vector2 attackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        visuals.PlayAction(attackClip, attackDirection, currentWeapon.Timeline.At(speed), true);
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
            visuals.StopAction();
        }
    }

    /// <summary>
    /// On the host, resolves the hit of an attack a client's player reached contact with toward
    /// <paramref name="direction"/> while standing at <paramref name="attackerPosition"/>, rather than around this
    /// lagging copy. Ignored while this machine sees the player dead. Returns how many units the hit reached.
    /// </summary>
    public int ResolveRemoteContact(int attackIndex, Vector2 direction, Vector2 attackerPosition)
    {
        if (attackService == null || currentWeapon == null || attackIndex != OnlyAttack || (health != null && health.IsDead))
        {
            return 0;
        }

        return attackService.Resolve(transform, currentWeapon, direction, attackerPosition);
    }

    /// <summary>
    /// Stops showing an attack that ended early and raises <see cref="AttackCancelled"/>.
    /// </summary>
    private void EndEarly()
    {
        if (visuals != null)
        {
            visuals.StopAction();
        }

        AttackCancelled?.Invoke();
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
