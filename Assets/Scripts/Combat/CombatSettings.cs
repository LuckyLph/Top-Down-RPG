using UnityEngine;

/// <summary>
/// Rules for resolving hits, the layers area effects and projectiles meet, the status catalog, and how popups look.
/// Registered in the Gameplay scope.
/// </summary>
[CreateAssetMenu(fileName = "CombatSettings", menuName = "TopDownRPG/Combat/Combat Settings")]
public sealed class CombatSettings : ScriptableObject
{
    [Header("Resolution")]
    [SerializeField, Range(-100, 0), Tooltip("Lowest effective resistance; -100 doubles damage.")]
    private int resistanceFloor = -100;
    [SerializeField, Range(0, 99), Tooltip("Highest effective resistance. Only immunities stop a hit completely.")]
    private int resistanceCap = 80;
    [SerializeField, Min(0), Tooltip("Least damage a hit that is not immune deals.")]
    private int minimumDamage = 1;

    [SerializeField, Tooltip("Layers that area effects look for units on.")]
    private LayerMask unitLayers = ~0;
    [SerializeField, Tooltip("Layers whose colliders stop projectiles, as walls do.")]
    private LayerMask projectileWallLayers;

    [Header("Statuses")]
    [SerializeField, Tooltip("Every status definition; a status is identified on the wire by its index here.")]
    private StatusEffectCatalog statusCatalog;

    [Header("Popups")]
    [SerializeField] private Color physicalColor = new(0.7f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color fireColor = new(1f, 0.5f, 0.1f, 1f);
    [SerializeField] private Color frostColor = new(0.45f, 0.8f, 1f, 1f);
    [SerializeField] private Color lightningColor = new(1f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color poisonColor = new(0.45f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color trueColor = new(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private Color healColor = new(0.3f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color immuneColor = new(0.75f, 0.75f, 0.75f, 1f);
    [SerializeField] private string immuneText = "Immune";
    [SerializeField, Range(0.3f, 1f), Tooltip("Size of damage-over-time popups relative to direct hits.")]
    private float periodicPopupScale = 0.75f;

    public int ResistanceFloor => resistanceFloor;
    public int ResistanceCap => resistanceCap;
    public int MinimumDamage => minimumDamage;
    public StatusEffectCatalog StatusCatalog => statusCatalog;
    public LayerMask UnitLayers => unitLayers;
    public LayerMask ProjectileWallLayers => projectileWallLayers;
    public Color HealColor => healColor;
    public Color ImmuneColor => immuneColor;
    public string ImmuneText => immuneText;
    public float PeriodicPopupScale => periodicPopupScale;

    /// <summary>
    /// Builds an unsaved, hidden runtime instance with default rules and <paramref name="catalog"/>, for tests that
    /// must not touch project assets.
    /// </summary>
    public static CombatSettings Create(StatusEffectCatalog catalog)
    {
        CombatSettings settings = CreateInstance<CombatSettings>();
        settings.statusCatalog = catalog;
        settings.hideFlags = HideFlags.HideAndDontSave;
        return settings;
    }

    /// <summary>
    /// The popup color for <paramref name="type"/> damage; the physical color for any type without its own.
    /// </summary>
    public Color GetColor(DamageType type)
    {
        switch (type)
        {
            case DamageType.Fire:
                return fireColor;
            case DamageType.Frost:
                return frostColor;
            case DamageType.Lightning:
                return lightningColor;
            case DamageType.Poison:
                return poisonColor;
            case DamageType.True:
                return trueColor;
            default:
                return physicalColor;
        }
    }
}
