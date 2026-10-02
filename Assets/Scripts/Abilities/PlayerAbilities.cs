using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The player's six ability slots, built from its <see cref="PlayerClass"/> (Q W E R) and equipped weapon (A S).
/// Checks and starts casts on the clock-driven cooldowns, hands each started cast to <see cref="AbilityService"/>
/// and raises cast events. Which cast runs when is decided by <see cref="PlayerOrders"/>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAbilities : MonoBehaviour, IAbilityCaster
{
    public const int ClassSlotCount = PlayerClass.AbilityCount;

    [SerializeField] private PlayerClass playerClass;
    [SerializeField] private PlayerWeaponController weaponController;

    private readonly AbilityDefinition[] slots = new AbilityDefinition[AbilitySlots.Count];
    private readonly AbilityCooldowns cooldowns = new(AbilitySlots.Count);
    private IClock clock = UnityClock.Shared;
    private AbilityService abilityService;
    private Health health;
    private int runningSlot = -1;

    public event Action<AbilityCast> CastStarted;
    public event Action<int> CastEnded;
    public event Action<int, CastOutcome> CastFailed;
    public event Action SlotsChanged;

    public PlayerClass Class => playerClass;
    public bool IsCasting => runningSlot >= 0;
    public int RunningSlot => runningSlot;

    private void Awake()
    {
        ResolveReferences();
        RebuildSlots();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (weaponController != null)
        {
            weaponController.EquippedWeaponChanged += HandleWeaponChanged;
        }

        RebuildSlots();
    }

    private void OnDisable()
    {
        if (weaponController != null)
        {
            weaponController.EquippedWeaponChanged -= HandleWeaponChanged;
        }
    }

    private void Start()
    {
        if (abilityService == null)
        {
            Debug.LogError($"{name} was not injected with an {nameof(AbilityService)}; its casts will have no effect.", this);
        }
    }

    [Inject]
    public void Construct(IClock gameClock, AbilityService service)
    {
        clock = gameClock ?? UnityClock.Shared;
        abilityService = service;
    }

    public AbilityDefinition GetAbility(int slot)
    {
        return slot >= 0 && slot < slots.Length ? slots[slot] : null;
    }

    public float RemainingCooldown(int slot)
    {
        return slot >= 0 && slot < slots.Length ? cooldowns.Remaining(slot, clock.Time) : 0f;
    }

    public bool CanCast(int slot, out CastOutcome failure)
    {
        if (GetAbility(slot) == null)
        {
            failure = CastOutcome.EmptySlot;
            return false;
        }

        if (health != null && health.IsDead)
        {
            failure = CastOutcome.Dead;
            return false;
        }

        if (!cooldowns.IsReady(slot, clock.Time))
        {
            failure = CastOutcome.OnCooldown;
            return false;
        }

        failure = CastOutcome.Started;
        return true;
    }

    /// <summary>
    /// Starts a cast in <paramref name="slot"/>: fails without side effects (raising <see cref="CastFailed"/>) when
    /// the slot is empty, on cooldown, another cast runs or the player is dead. Otherwise starts the cooldown,
    /// raises <see cref="CastStarted"/> and hands the cast to the <see cref="AbilityService"/>.
    /// </summary>
    public CastOutcome TryCast(int slot, in CastAim aim)
    {
        if (!CanCast(slot, out CastOutcome failure))
        {
            ReportFailure(slot, failure);
            return failure;
        }

        if (IsCasting)
        {
            ReportFailure(slot, CastOutcome.CastInProgress);
            return CastOutcome.CastInProgress;
        }

        AbilityDefinition ability = slots[slot];
        cooldowns.Start(slot, clock.Time, ability.Cooldown);
        runningSlot = slot;
        AbilityCast cast = new(slot, ability, aim);
        CastStarted?.Invoke(cast);
        if (abilityService != null)
        {
            abilityService.Apply(this, cast);
        }

        return CastOutcome.Started;
    }

    public void EndCast(int slot)
    {
        if (runningSlot != slot)
        {
            return;
        }

        runningSlot = -1;
        CastEnded?.Invoke(slot);
    }

    public void ReportFailure(int slot, CastOutcome reason)
    {
        CastFailed?.Invoke(slot, reason);
    }

    /// <summary>
    /// Replays a cast another machine started for this player: no cooldown or events here, but the
    /// <see cref="AbilityService"/> applies it if this machine is the authority.
    /// </summary>
    public void PlayRemoteCast(int slot, in CastAim aim)
    {
        AbilityDefinition ability = GetAbility(slot);
        if (ability != null && abilityService != null)
        {
            abilityService.Apply(this, new AbilityCast(slot, ability, aim));
        }
    }

    /// <summary>
    /// Rebuilds the six slots from the class and the equipped weapon, raising <see cref="SlotsChanged"/>.
    /// </summary>
    public void RebuildSlots()
    {
        PlayerWeapon weapon = weaponController != null ? weaponController.CurrentWeapon : null;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = i < ClassSlotCount
                ? (playerClass != null ? playerClass.GetAbility(i) : null)
                : (weapon != null ? weapon.GetAbility(i - ClassSlotCount) : null);
        }

        SlotsChanged?.Invoke();
    }

    /// <summary>
    /// Test setup without the engine lifecycle: sets the class and does what <c>OnEnable</c> does.
    /// </summary>
    internal void Configure(PlayerClass classAsset)
    {
        playerClass = classAsset;
        OnDisable();
        OnEnable();
    }

    private void HandleWeaponChanged(PlayerWeapon _)
    {
        RebuildSlots();
    }

    private void ResolveReferences()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }

        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
