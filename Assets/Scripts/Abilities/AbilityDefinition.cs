using UnityEngine;

/// <summary>
/// Describes how an ability is cast (cooldown, cast time, targeting, range, movement during the cast), its effect: a
/// <see cref="Hit"/> (damage and statuses) and a heal, applied by <see cref="AbilityService"/> on the host when the cast
/// completes, to the units its targeting and <see cref="AreaRadius"/> select; and how its cast looks (Docs/Animation.md,
/// Phase 5): the clips the caster plays, standing and over the run, the release and follow-through after the cast
/// completes, and whether a long cast channels.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Abilities/Ability Definition", fileName = "Ability")]
public sealed class AbilityDefinition : ScriptableObject
{
    [Header("HUD")]
    [SerializeField] private string displayName = "Ability";
    [SerializeField] private Sprite icon;

    [Header("Timing")]
    [SerializeField, Min(0f), Tooltip("Seconds, starting when the cast starts.")]
    private float cooldown = 1f;

    [SerializeField, Min(0f), Tooltip("Seconds spent casting. 0 is instant.")]
    private float castTime;

    [Header("Targeting")]
    [SerializeField] private AbilityTargeting targeting = AbilityTargeting.Direction;

    [SerializeField, Tooltip("Units a Unit-targeted cast accepts. Ignored for other targeting.")]
    private AbilityUnitFilter unitFilter = AbilityUnitFilter.Enemy;

    [SerializeField, Min(0f), Tooltip("Maximum range for Point and Unit targeting (collider distance for units), and the length of a Direction ability's line.")]
    private float range = 3f;

    [Header("Casting")]
    [SerializeField, Tooltip("Stop: stand still. Continue: the paused order keeps moving the caster. Ability: the ability will move the caster (stands still for now).")]
    private CastMovement castMovement = CastMovement.Stop;

    [SerializeField, Tooltip("Pressing it while another cast runs queues it instead of failing.")]
    private bool bufferable;

    [Header("Effect")]
    [SerializeField, Tooltip("Damage and statuses applied to each affected unit. Damage and debuffs only land on enemies, buffs on allies (a buff still lands on an ally whose damage is refused).")]
    private Hit hit;

    [SerializeField, Min(0), Tooltip("HP restored to each affected unit. Only lands on allies.")]
    private int heal;

    [SerializeField, Min(0f), Tooltip("None: radius around the caster (0 = the caster only). Point: radius around the point. Direction: half-width of the line, which is Range long. Unused for Unit.")]
    private float areaRadius;

    [Header("Animation")]
    [SerializeField, Tooltip("The clip the caster plays, by name in its character's animation set. Empty for none.")]
    private string castClip;

    [SerializeField, Tooltip("The same cast drawn over the run's legs, shown while a Continue cast moves the caster. Empty for none.")]
    private string movingCastClip;

    [SerializeField, Min(0.01f), Tooltip("Seconds the release frame shows from the moment the cast completes.")]
    private float releaseSeconds = 0.06f;

    [SerializeField, Min(0f), Tooltip("Seconds of follow-through after the release. Moving cuts it short, unless the moving clip plays it over the legs.")]
    private float followThroughSeconds = 0.24f;

    [SerializeField, Min(0f), Tooltip("0: the gather stretches over the whole cast time. Otherwise a cast longer than its clip's gather plays the gather at its natural speed, then shows its last two gather frames in turn, this many seconds each, until the release.")]
    private float channelBeat;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public float Cooldown => cooldown;
    public float CastTime => castTime;
    public AbilityTargeting Targeting => targeting;
    public AbilityUnitFilter UnitFilter => unitFilter;
    public float Range => range;
    public CastMovement CastMovement => castMovement;
    public bool Bufferable => bufferable;
    public Hit Hit => hit;
    public int Heal => Mathf.Max(0, heal);
    public float AreaRadius => Mathf.Max(0f, areaRadius);
    public string CastClip => castClip;
    public string MovingCastClip => movingCastClip;
    public float ChannelBeat => Mathf.Max(0f, channelBeat);

    /// <summary>
    /// The cast's animation timeline: the gather as long as the cast time, so the release frame shows when the cast
    /// completes, then the release and the follow-through.
    /// </summary>
    public AttackTimeline CastTimeline => new(castTime, Mathf.Max(0.01f, releaseSeconds), Mathf.Max(0f, followThroughSeconds));

    /// <summary>
    /// Whether casting it does anything to its target: damage, statuses or a heal.
    /// </summary>
    public bool HasEffect => hit.Amount > 0 || hit.StatusCount > 0 || Heal > 0;

    /// <summary>
    /// Whether a unit of <paramref name="team"/> can be the target of this ability.
    /// </summary>
    public bool Accepts(UnitTeam team)
    {
        return unitFilter switch
        {
            AbilityUnitFilter.Enemy => team == UnitTeam.Enemy,
            AbilityUnitFilter.Ally => team == UnitTeam.Ally,
            _ => team == UnitTeam.Enemy || team == UnitTeam.Ally
        };
    }

    /// <summary>
    /// Builds an unsaved, hidden runtime instance with the given values, for tests that must not touch project
    /// assets. The caller owns it and destroys it.
    /// </summary>
    public static AbilityDefinition Create(
        string name,
        float cooldown = 1f,
        float castTime = 0f,
        AbilityTargeting targeting = AbilityTargeting.Direction,
        AbilityUnitFilter unitFilter = AbilityUnitFilter.Enemy,
        float range = 3f,
        CastMovement castMovement = CastMovement.Stop,
        bool bufferable = false,
        Hit hit = default,
        int heal = 0,
        float areaRadius = 0f,
        string castClip = null,
        string movingCastClip = null,
        float releaseSeconds = 0.06f,
        float followThroughSeconds = 0.24f,
        float channelBeat = 0f)
    {
        AbilityDefinition ability = CreateInstance<AbilityDefinition>();
        ability.name = name;
        ability.displayName = name;
        ability.cooldown = cooldown;
        ability.castTime = castTime;
        ability.targeting = targeting;
        ability.unitFilter = unitFilter;
        ability.range = range;
        ability.castMovement = castMovement;
        ability.bufferable = bufferable;
        ability.hit = hit;
        ability.heal = heal;
        ability.areaRadius = areaRadius;
        ability.castClip = castClip;
        ability.movingCastClip = movingCastClip;
        ability.releaseSeconds = releaseSeconds;
        ability.followThroughSeconds = followThroughSeconds;
        ability.channelBeat = channelBeat;
        ability.hideFlags = HideFlags.HideAndDontSave;
        return ability;
    }
}
