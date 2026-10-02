using UnityEngine;

/// <summary>
/// Describes how an ability is cast: cooldown, cast time, targeting, range and movement during the cast. It has
/// no effect yet; effects will be applied by <see cref="AbilityService"/>.
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

    [SerializeField, Min(0f), Tooltip("Maximum range for Point and Unit targeting (collider distance for units).")]
    private float range = 3f;

    [Header("Casting")]
    [SerializeField, Tooltip("Stop: stand still. Continue: the paused order keeps moving the caster. Ability: the ability will move the caster (stands still for now).")]
    private CastMovement castMovement = CastMovement.Stop;

    [SerializeField, Tooltip("Pressing it while another cast runs queues it instead of failing.")]
    private bool bufferable;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public float Cooldown => cooldown;
    public float CastTime => castTime;
    public AbilityTargeting Targeting => targeting;
    public AbilityUnitFilter UnitFilter => unitFilter;
    public float Range => range;
    public CastMovement CastMovement => castMovement;
    public bool Bufferable => bufferable;

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

    public static AbilityDefinition Create(
        string name,
        float cooldown = 1f,
        float castTime = 0f,
        AbilityTargeting targeting = AbilityTargeting.Direction,
        AbilityUnitFilter unitFilter = AbilityUnitFilter.Enemy,
        float range = 3f,
        CastMovement castMovement = CastMovement.Stop,
        bool bufferable = false)
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
        ability.hideFlags = HideFlags.HideAndDontSave;
        return ability;
    }
}
