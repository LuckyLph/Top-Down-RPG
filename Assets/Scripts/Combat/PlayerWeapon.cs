using UnityEngine;

/// <summary>
/// A weapon the player can equip: the <see cref="Hit"/> each swing deals, the attack cooldown and range, how its
/// slash prefab is placed around the player, its HUD icon, and the two abilities it grants (the A and S slots).
/// </summary>
[CreateAssetMenu(fileName = "Player Weapon", menuName = "TopDownRPG/Combat/Player Weapon")]
public sealed class PlayerWeapon : ScriptableObject
{
    public const int AbilityCount = 2;
    private const int DirectionalSpawnOffsetCount = 4;

    [SerializeField] private string displayName = "Sword";
    [SerializeField, Min(0)] private int damage = 1;
    [SerializeField] private DamageType damageType = DamageType.Physical;
    [SerializeField, Tooltip("Statuses each swing applies to a target that survives it.")]
    private StatusEffectDefinition[] onHitStatuses = new StatusEffectDefinition[0];
    [SerializeField, Min(0.01f)] private float attackCooldown = 0.35f;
    [SerializeField, Min(0f), Tooltip("Collider-to-collider distance within which an attack order stops chasing and swings.")]
    private float attackRange = 0.3f;
    [SerializeField, Min(0f)] private float slashSpawnDistance = 0.55f;
    [SerializeField, Tooltip("World-space directional offsets ordered Down, Up, Left, Right.")]
    private Vector2[] slashSpawnOffsets = new Vector2[DirectionalSpawnOffsetCount];
    [SerializeField] private GameObject slashPrefab;
    [SerializeField] private Sprite hudIcon;
    [SerializeField, Tooltip("Exactly two abilities, for the A and S slots. Empty entries are empty slots.")]
    private AbilityDefinition[] abilities = new AbilityDefinition[AbilityCount];

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Weapon" : displayName;
    public int Damage => Mathf.Max(0, damage);
    public DamageType DamageType => damageType;
    public Hit Hit => new(Damage, damageType, onHitStatuses);
    public float AttackCooldown => Mathf.Max(0.01f, attackCooldown);
    public float AttackRange => Mathf.Max(0f, attackRange);
    public float SlashSpawnDistance => Mathf.Max(0f, slashSpawnDistance);
    public GameObject SlashPrefab => slashPrefab;
    public Sprite HudIcon => hudIcon;

    /// <summary>
    /// Keeps exactly four directional slash offsets and <see cref="AbilityCount"/> ability entries in the inspector.
    /// </summary>
    private void OnValidate()
    {
        EnsureDirectionalSpawnOffsets();
        abilities = AbilitySlotArrays.Resize(abilities, AbilityCount);
    }

    /// <summary>
    /// The ability for weapon slot <paramref name="index"/> (0 is A), or null when empty or out of range.
    /// </summary>
    public AbilityDefinition GetAbility(int index)
    {
        return abilities != null && index >= 0 && index < abilities.Length ? abilities[index] : null;
    }

    /// <summary>
    /// The extra world-space slash offset for the cardinal direction nearest <paramref name="attackDirection"/>;
    /// horizontal wins only when strictly dominant, and a zero direction counts as down.
    /// </summary>
    public Vector2 GetSlashSpawnOffset(Vector2 attackDirection)
    {
        EnsureDirectionalSpawnOffsets();
        return slashSpawnOffsets[GetDirectionIndex(attackDirection)];
    }

    /// <summary>
    /// Builds an unsaved, hidden runtime weapon with the given values, for tests that must not touch project assets.
    /// Offsets and abilities are truncated or padded to their fixed counts.
    /// </summary>
    public static PlayerWeapon Create(
        string name,
        int attackDamage,
        float cooldown,
        float spawnDistance,
        Vector2[] spawnOffsets,
        GameObject prefab = null,
        Sprite icon = null,
        float range = 0.3f,
        AbilityDefinition[] weaponAbilities = null,
        DamageType type = DamageType.Physical,
        StatusEffectDefinition[] statuses = null)
    {
        PlayerWeapon weapon = CreateInstance<PlayerWeapon>();
        weapon.displayName = name;
        weapon.damage = attackDamage;
        weapon.damageType = type;
        weapon.onHitStatuses = statuses ?? new StatusEffectDefinition[0];
        weapon.attackCooldown = cooldown;
        weapon.slashSpawnDistance = spawnDistance;
        weapon.slashSpawnOffsets = NormalizeDirectionalSpawnOffsets(spawnOffsets);
        weapon.slashPrefab = prefab;
        weapon.hudIcon = icon;
        weapon.attackRange = range;
        weapon.abilities = AbilitySlotArrays.Resize(weaponAbilities, AbilityCount);
        weapon.hideFlags = HideFlags.HideAndDontSave;
        return weapon;
    }

    /// <summary>
    /// Replaces the slash offsets with a copy holding exactly four entries.
    /// </summary>
    private void EnsureDirectionalSpawnOffsets()
    {
        slashSpawnOffsets = NormalizeDirectionalSpawnOffsets(slashSpawnOffsets);
    }

    /// <summary>
    /// A new four-entry array (Down, Up, Left, Right) copied from <paramref name="offsets"/>, truncated or padded with
    /// zero offsets; all zero when it is null.
    /// </summary>
    private static Vector2[] NormalizeDirectionalSpawnOffsets(Vector2[] offsets)
    {
        Vector2[] normalizedOffsets = new Vector2[DirectionalSpawnOffsetCount];
        if (offsets == null)
        {
            return normalizedOffsets;
        }

        int copyCount = Mathf.Min(offsets.Length, DirectionalSpawnOffsetCount);
        for (int i = 0; i < copyCount; i++)
        {
            normalizedOffsets[i] = offsets[i];
        }

        return normalizedOffsets;
    }

    /// <summary>
    /// The offset index of the dominant axis of <paramref name="attackDirection"/>: 0 Down, 1 Up, 2 Left, 3 Right.
    /// Ties and zero go vertical, and a non-positive vertical component is Down.
    /// </summary>
    private static int GetDirectionIndex(Vector2 attackDirection)
    {
        if (Mathf.Abs(attackDirection.x) > Mathf.Abs(attackDirection.y))
        {
            return attackDirection.x < 0f ? 2 : 3;
        }

        return attackDirection.y > 0f ? 1 : 0;
    }
}
