using System;
using UnityEngine;
using VContainer;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The player's six ability slots, built from its <see cref="PlayerClass"/> (Q W E R) and equipped weapon (A S).
/// Checks and starts casts on the clock-driven cooldowns, hands each started cast to <see cref="AbilityService"/>
/// and raises cast events. Which cast runs when is decided by <see cref="PlayerOrders"/>. Draws gizmos for recent
/// casts (local and remote) and, when selected, the ranges of its Point and Unit abilities.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAbilities : MonoBehaviour, IAbilityCaster
{
    public const int ClassSlotCount = PlayerClass.AbilityCount;

    private const int RecentCastCapacity = 8;
    private const float DirectionGizmoLength = 2f;
    private const float LabelHeight = 2.3f;
    private const float LabelSpacing = 0.25f;
    private const int CircleSegments = 32;

    private static readonly Color[] SlotColors =
    {
        new(1f, 0.85f, 0.2f),
        new(0.3f, 0.85f, 1f),
        new(0.4f, 1f, 0.45f),
        new(1f, 0.35f, 0.3f),
        new(0.85f, 0.5f, 1f),
        new(1f, 0.6f, 0.2f)
    };

    [SerializeField] private PlayerClass playerClass;
    [SerializeField] private PlayerWeaponController weaponController;

    [Header("Debug")]
    [SerializeField, Tooltip("Draw each cast's aim while it runs, fading out afterwards. Remote casts are dimmer.")]
    private bool drawCastGizmos = true;
    [SerializeField, Tooltip("When selected, draw the range of every Point and Unit ability.")]
    private bool drawRangeGizmos = true;
    [SerializeField, Min(0f), Tooltip("Seconds a cast's gizmo lingers after its cast time.")]
    private float castGizmoSeconds = 1f;

    private readonly AbilityDefinition[] slots = new AbilityDefinition[AbilitySlots.Count];
    private readonly AbilityCooldowns cooldowns = new(AbilitySlots.Count);
    private IClock clock = UnityClock.Shared;
    private AbilityService abilityService;
    private Health health;
    private int runningSlot = -1;
    private readonly CastRecord[] recentCasts = new CastRecord[RecentCastCapacity];
    private int nextRecentCast;

    public event Action<AbilityCast> CastStarted;
    public event Action<int> CastEnded;
    public event Action<int, CastOutcome> CastFailed;
    public event Action SlotsChanged;

    public PlayerClass Class => playerClass;
    public bool IsCasting => runningSlot >= 0;
    public int RunningSlot => runningSlot;

    internal int RecentCastCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < recentCasts.Length; i++)
            {
                if (recentCasts[i].Valid)
                {
                    count++;
                }
            }

            return count;
        }
    }

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
        RecordCast(cast, false);
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
        if (ability == null)
        {
            return;
        }

        AbilityCast cast = new(slot, ability, aim);
        RecordCast(cast, true);
        if (abilityService != null)
        {
            abilityService.Apply(this, cast);
        }
    }

    /// <summary>
    /// The <paramref name="age"/>-th most recent recorded cast (0 is the newest), for tests and gizmos.
    /// </summary>
    internal bool TryGetRecentCast(int age, out AbilityCast cast, out bool remote)
    {
        if (age < 0 || age >= RecentCastCapacity)
        {
            cast = default;
            remote = false;
            return false;
        }

        CastRecord record = recentCasts[RecentIndex(age)];
        cast = record.Cast;
        remote = record.Remote;
        return record.Valid;
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

    private int RecentIndex(int age)
    {
        return (nextRecentCast - 1 - age + RecentCastCapacity * 2) % RecentCastCapacity;
    }

    private void RecordCast(in AbilityCast cast, bool remote)
    {
        recentCasts[nextRecentCast] = new CastRecord(cast, clock.Time, remote);
        nextRecentCast = (nextRecentCast + 1) % RecentCastCapacity;
    }

    private void OnDrawGizmos()
    {
        if (!drawCastGizmos)
        {
            return;
        }

        float now = clock.Time;
        Vector3 origin = transform.position;
        int shown = 0;
        for (int age = 0; age < RecentCastCapacity; age++)
        {
            CastRecord record = recentCasts[RecentIndex(age)];
            if (!record.Valid)
            {
                continue;
            }

            AbilityCast cast = record.Cast;
            bool remote = record.Remote;
            float castEnd = record.StartTime + cast.Ability.CastTime;
            float sinceEnd = now - castEnd;
            if (sinceEnd > castGizmoSeconds)
            {
                continue;
            }

            float alpha = sinceEnd <= 0f || castGizmoSeconds <= 0f ? 1f : 1f - sinceEnd / castGizmoSeconds;
            Color color = SlotColor(cast.Slot);
            color.a = alpha * (remote ? 0.55f : 1f);
            Gizmos.color = color;
            DrawAim(origin, cast);

#if UNITY_EDITOR
            Handles.color = color;
            string timing = sinceEnd < 0f ? $" {-sinceEnd:0.0}s" : string.Empty;
            string source = remote ? " (remote)" : string.Empty;
            Handles.Label(origin + Vector3.up * (LabelHeight + shown * LabelSpacing), $"{AbilitySlots.KeyName(cast.Slot)} {cast.Ability.DisplayName}{timing}{source}");
#endif
            shown++;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawRangeGizmos)
        {
            return;
        }

        Vector3 origin = transform.position;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            AbilityDefinition ability = slots[slot];
            if (ability == null || (ability.Targeting != AbilityTargeting.Point && ability.Targeting != AbilityTargeting.Unit))
            {
                continue;
            }

            Color color = SlotColor(slot);
            color.a = 0.35f;
            Gizmos.color = color;
            DrawCircle(origin, ability.Range);

#if UNITY_EDITOR
            Handles.color = color;
            Handles.Label(origin + Vector3.right * ability.Range, $"{AbilitySlots.KeyName(slot)} {ability.Range:0.#}");
#endif
        }
    }

    private static void DrawAim(Vector3 origin, in AbilityCast cast)
    {
        CastAim aim = cast.Aim;
        switch (cast.Ability.Targeting)
        {
            case AbilityTargeting.None:
                DrawCircle(origin, 0.6f);
                break;
            case AbilityTargeting.Direction:
                Vector3 end = origin + (Vector3)(aim.Direction * DirectionGizmoLength);
                Gizmos.DrawLine(origin, end);
                DrawCircle(end, 0.1f);
                break;
            case AbilityTargeting.Point:
                Gizmos.DrawLine(origin, aim.Point);
                DrawCircle(aim.Point, 0.25f);
                break;
            default:
                Vector3 target = aim.Target.Health != null ? aim.Target.Health.transform.position : (Vector3)aim.Point;
                Gizmos.DrawLine(origin, target);
                DrawCircle(target, 0.4f);
                break;
        }
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= CircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / CircleSegments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private static Color SlotColor(int slot)
    {
        return slot >= 0 && slot < SlotColors.Length ? SlotColors[slot] : Color.white;
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

    private readonly struct CastRecord
    {
        public CastRecord(AbilityCast cast, float startTime, bool remote)
        {
            Cast = cast;
            StartTime = startTime;
            Remote = remote;
            Valid = true;
        }

        public AbilityCast Cast { get; }
        public float StartTime { get; }
        public bool Remote { get; }
        public bool Valid { get; }
    }
}
