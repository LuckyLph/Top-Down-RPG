using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The player's equipped weapon: swings it as a slash from <see cref="SlashSpawner"/>, gated by the weapon's attack
/// cooldown on the game clock, and raises <see cref="Attacked"/> so the swing can be replicated. Equips the starting
/// weapon on <c>Awake</c>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponController : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerWeapon startingWeapon;

    private SlashSpawner slashSpawner;
    private IClock clock = UnityClock.Shared;
    private PlayerWeapon currentWeapon;
    private SpriteRenderer ownerSpriteRenderer;
    private float nextAttackTime;

    public event Action<PlayerWeapon> EquippedWeaponChanged;
    public event Action<Vector2> Attacked;

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

    /// <summary>
    /// Finds the player controller and the sprite renderer slashes anchor and sort against, then equips the starting
    /// weapon.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        ownerSpriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        Equip(startingWeapon);
    }

    /// <summary>
    /// Fills an empty player controller reference from this object.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Takes the slash spawner and the game clock that times the attack cooldown; null falls back to the shared
    /// Unity clock.
    /// </summary>
    [Inject]
    public void Construct(SlashSpawner spawner, IClock gameClock)
    {
        slashSpawner = spawner;
        clock = gameClock ?? UnityClock.Shared;
    }

    /// <summary>
    /// Makes <paramref name="weapon"/> (null for none) the current weapon and raises
    /// <see cref="EquippedWeaponChanged"/>.
    /// </summary>
    public void Equip(PlayerWeapon weapon)
    {
        ResolveReferences();
        currentWeapon = weapon;
        EquippedWeaponChanged?.Invoke(currentWeapon);
    }

    /// <summary>
    /// Swings toward <paramref name="direction"/> (the facing direction when it is zero) if the cooldown allows.
    /// </summary>
    public bool TryAttack(Vector2 direction)
    {
        ResolveReferences();

        if (currentWeapon == null || clock.Time < nextAttackTime)
        {
            return false;
        }

        if (slashSpawner == null)
        {
            Debug.LogError($"{nameof(PlayerWeaponController)} was not injected with a {nameof(SlashSpawner)}.", this);
            return false;
        }

        Vector2 attackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : CurrentFacingDirection;
        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = Vector2.down;
        }

        if (slashSpawner.Spawn(transform, currentWeapon, attackDirection, ownerSpriteRenderer) == null)
        {
            return false;
        }

        nextAttackTime = clock.Time + currentWeapon.AttackCooldown;
        Attacked?.Invoke(attackDirection);
        return true;
    }

    /// <summary>
    /// Replays a swing the owner made on another machine toward <paramref name="direction"/> (down when zero): spawns
    /// the slash without the cooldown or <see cref="Attacked"/>. Its hits only land where this machine is the
    /// authority.
    /// </summary>
    public void PlayRemoteAttack(Vector2 direction)
    {
        if (currentWeapon == null || slashSpawner == null)
        {
            return;
        }

        Vector2 attackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        slashSpawner.Spawn(transform, currentWeapon, attackDirection, ownerSpriteRenderer);
    }

    /// <summary>
    /// Falls back to the <see cref="PlayerController"/> on this object when none is assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }
    }
}
