using UnityEngine;

[CreateAssetMenu(fileName = "Player Weapon", menuName = "TopDownRPG/Combat/Player Weapon")]
public sealed class PlayerWeapon : ScriptableObject
{
    public const int AbilityCount = 2;
    private const int DirectionalSpawnOffsetCount = 4;

    [SerializeField] private string displayName = "Sword";
    [SerializeField, Min(0)] private int damage = 1;
    [SerializeField] private DamageType damageType = DamageType.Physical;
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
    public float AttackCooldown => Mathf.Max(0.01f, attackCooldown);
    public float AttackRange => Mathf.Max(0f, attackRange);
    public float SlashSpawnDistance => Mathf.Max(0f, slashSpawnDistance);
    public GameObject SlashPrefab => slashPrefab;
    public Sprite HudIcon => hudIcon;

    private void OnValidate()
    {
        EnsureDirectionalSpawnOffsets();
        abilities = AbilitySlotArrays.Resize(abilities, AbilityCount);
    }

    public AbilityDefinition GetAbility(int index)
    {
        return abilities != null && index >= 0 && index < abilities.Length ? abilities[index] : null;
    }

    public Vector2 GetSlashSpawnOffset(Vector2 attackDirection)
    {
        EnsureDirectionalSpawnOffsets();
        return slashSpawnOffsets[GetDirectionIndex(attackDirection)];
    }

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
        DamageType type = DamageType.Physical)
    {
        PlayerWeapon weapon = CreateInstance<PlayerWeapon>();
        weapon.displayName = name;
        weapon.damage = attackDamage;
        weapon.damageType = type;
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

    private void EnsureDirectionalSpawnOffsets()
    {
        slashSpawnOffsets = NormalizeDirectionalSpawnOffsets(slashSpawnOffsets);
    }

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

    private static int GetDirectionIndex(Vector2 attackDirection)
    {
        if (Mathf.Abs(attackDirection.x) > Mathf.Abs(attackDirection.y))
        {
            return attackDirection.x < 0f ? 2 : 3;
        }

        return attackDirection.y > 0f ? 1 : 0;
    }
}
