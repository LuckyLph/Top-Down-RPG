using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// How a weapon's hit reaches its target at contact: a melee area searched at once, or a projectile that leaves then
/// (Docs/Animation.md, Phase 4).
/// </summary>
public enum AttackDelivery
{
    Melee,
    Projectile
}

/// <summary>
/// A weapon the player can equip: the <see cref="Hit"/> each attack deals, the attack's cooldown, range and timeline
/// (windup, contact and recovery at attack speed 1) with its cancel rules, how its hit lands at contact (the area a
/// melee weapon searches, or the projectile a ranged one launches), the clip of the character's animation set its
/// attack plays (the only frames that draw it), its HUD icon, and the two abilities it grants (the A and S slots). A
/// weapon is named on the wire by its index in the <see cref="WeaponCatalog"/>, and in saves by its id, its asset's
/// GUID, which the editor keeps.
/// </summary>
[CreateAssetMenu(fileName = "Player Weapon", menuName = "TopDownRPG/Combat/Player Weapon")]
public sealed class PlayerWeapon : ScriptableObject
{
    public const int AbilityCount = 2;
    private const int DirectionalOffsetCount = 4;

    [SerializeField, Tooltip("Stable id of this weapon in saves, its asset's GUID; set by the editor.")]
    private string weaponId;
    [SerializeField] private string displayName = "Sword";
    [SerializeField, Min(0)] private int damage = 1;
    [SerializeField] private DamageType damageType = DamageType.Physical;
    [SerializeField, Tooltip("Statuses each attack applies to a target that survives it.")]
    private StatusEffectDefinition[] onHitStatuses = new StatusEffectDefinition[0];

    [Header("Attack")]
    [SerializeField, Min(0.01f), Tooltip("Seconds from one attack's start to the next one's earliest start, at attack speed 1.")]
    private float attackCooldown = 0.35f;
    [SerializeField, Min(0f), Tooltip("Collider-to-collider distance within which an attack order stops chasing and swings.")]
    private float attackRange = 0.3f;
    [SerializeField, Min(0f), Tooltip("Seconds from the attack's start to contact, when its hit lands, at attack speed 1.")]
    private float windupSeconds = 0.15f;
    [SerializeField, Min(0.01f), Tooltip("Seconds the contact frame holds once the hit has landed, at attack speed 1.")]
    private float contactSeconds = 0.05f;
    [SerializeField, Min(0f), Tooltip("Seconds of follow-through after the contact, at attack speed 1.")]
    private float recoverySeconds = 0.15f;
    [SerializeField, Tooltip("A move order or Stop during the windup aborts the attack: nothing lands and the cooldown is given back.")]
    private bool windupCancelledByMove = true;
    [SerializeField, Tooltip("Any order during the recovery ends it at once, and the next attack may start in it.")]
    private bool recoveryCancellable = true;

    [Header("Hit area")]
    [SerializeField, Tooltip("Melee: the area below is searched at contact. Projectile: a projectile leaves at contact and hits the first unit it can.")]
    private AttackDelivery delivery = AttackDelivery.Melee;
    [FormerlySerializedAs("slashSpawnDistance")]
    [SerializeField, Min(0f), Tooltip("How far along the attack's direction the hit area's centre lies from its offset point.")]
    private float hitDistance = 0.65f;
    [FormerlySerializedAs("slashSpawnOffsets")]
    [SerializeField, Tooltip("World-space offsets from the attacker's feet to the hit area's offset point, ordered Down, Up, Left, Right.")]
    private Vector2[] hitOffsets = new Vector2[DirectionalOffsetCount];
    [SerializeField, Tooltip("The hit area's width across the attack's direction and its depth along it, in units.")]
    private Vector2 hitSize = new(1.77f, 1.56f);

    [Header("Projectile")]
    [SerializeField, Min(0.1f), Tooltip("How fast the projectile flies, in units per second.")]
    private float projectileSpeed = 18f;
    [SerializeField, Min(0f), Tooltip("How far it flies at most from the shooter's feet, in units.")]
    private float projectileRange = 6.5f;
    [SerializeField, Min(0.01f), Tooltip("The radius it sweeps as it flies, in units.")]
    private float projectileRadius = 0.1f;
    [SerializeField, Min(0f), Tooltip("How far ahead of the shooter's feet it leaves, in units: where the bow is at release.")]
    private float projectileLaunchDistance = 0.5f;
    [SerializeField, Tooltip("It turns toward the attack's target as it flies and only hits it; without a living target it flies straight.")]
    private bool projectileHoming = true;
    [SerializeField, Tooltip("How it looks in flight on every machine.")]
    private ProjectileVisual projectileVisual;

    [Header("Animation")]
    [SerializeField, Tooltip("The clip of the character's animation set the attack plays, stretched to its timeline. The weapon only shows in it.")]
    private string attackClip = "SwordSlash";

    [Header("Presentation")]
    [SerializeField] private Sprite hudIcon;
    [SerializeField, Tooltip("Exactly two abilities, for the A and S slots. Empty entries are empty slots.")]
    private AbilityDefinition[] abilities = new AbilityDefinition[AbilityCount];

    public string WeaponId => weaponId ?? string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Weapon" : displayName;
    public int Damage => Mathf.Max(0, damage);
    public DamageType DamageType => damageType;
    public Hit Hit => new(Damage, damageType, onHitStatuses);
    public float AttackCooldown => Mathf.Max(0.01f, attackCooldown);
    public float AttackRange => Mathf.Max(0f, attackRange);
    public AttackTimeline Timeline => new(windupSeconds, Mathf.Max(0.01f, contactSeconds), recoverySeconds);
    public bool WindupCancelledByMove => windupCancelledByMove;
    public bool RecoveryCancellable => recoveryCancellable;
    public AttackDelivery Delivery => delivery;
    public bool FiresProjectile => delivery == AttackDelivery.Projectile;
    public ProjectileSpec Projectile => new(projectileSpeed, projectileRange, projectileRadius, projectileLaunchDistance, projectileHoming, projectileVisual);
    public float HitDistance => Mathf.Max(0f, hitDistance);
    public Vector2 HitSize => new(Mathf.Max(0f, hitSize.x), Mathf.Max(0f, hitSize.y));
    public string AttackClip => attackClip;
    public Sprite HudIcon => hudIcon;

    /// <summary>
    /// Keeps exactly four directional hit offsets and <see cref="AbilityCount"/> ability entries in the inspector, and
    /// the id equal to the asset's GUID, so a copied asset gets its own.
    /// </summary>
    private void OnValidate()
    {
        EnsureDirectionalOffsets();
        abilities = AbilitySlotArrays.Resize(abilities, AbilityCount);
#if UNITY_EDITOR
        string path = AssetDatabase.GetAssetPath(this);
        string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        if (!string.IsNullOrEmpty(guid) && guid != weaponId)
        {
            weaponId = guid;
            EditorUtility.SetDirty(this);
        }
#endif
    }

    /// <summary>
    /// The ability for weapon slot <paramref name="index"/> (0 is A), or null when empty or out of range.
    /// </summary>
    public AbilityDefinition GetAbility(int index)
    {
        return abilities != null && index >= 0 && index < abilities.Length ? abilities[index] : null;
    }

    /// <summary>
    /// The world-space offset from the feet for the cardinal direction nearest <paramref name="attackDirection"/>;
    /// horizontal wins only when strictly dominant, and a zero direction counts as down.
    /// </summary>
    public Vector2 HitOffset(Vector2 attackDirection)
    {
        EnsureDirectionalOffsets();
        return hitOffsets[GetDirectionIndex(attackDirection)];
    }

    /// <summary>
    /// The centre of the hit area for an attacker standing at <paramref name="attackerPosition"/> and striking toward
    /// the unit direction <paramref name="attackDirection"/>: its direction's offset, then the hit distance along it.
    /// </summary>
    public Vector2 HitCentre(Vector2 attackerPosition, Vector2 attackDirection)
    {
        return attackerPosition + HitOffset(attackDirection) + attackDirection * HitDistance;
    }

    /// <summary>
    /// Builds an unsaved, hidden runtime weapon with the given values, for tests that must not touch project assets, its
    /// id <c>test-</c> and its name.
    /// Offsets and abilities are truncated or padded to their fixed counts; the timeline defaults to 0.15 s, 0.05 s and
    /// 0.15 s with both cancel rules on, the hit area to 0.9 by 0.9, and the attack clip to the sword's. With a
    /// <paramref name="projectile"/> it fires that projectile instead of searching its area.
    /// </summary>
    public static PlayerWeapon Create(
        string name,
        int attackDamage,
        float cooldown,
        float distance,
        Vector2[] offsets,
        Sprite icon = null,
        float range = 0.3f,
        AbilityDefinition[] weaponAbilities = null,
        DamageType type = DamageType.Physical,
        StatusEffectDefinition[] statuses = null,
        Vector2? size = null,
        float windup = 0.15f,
        float contact = 0.05f,
        float recovery = 0.15f,
        bool cancelWindupByMove = true,
        bool cancellableRecovery = true,
        string attackClipName = "SwordSlash",
        ProjectileSpec? projectile = null)
    {
        PlayerWeapon weapon = CreateInstance<PlayerWeapon>();
        weapon.weaponId = $"test-{name}";
        weapon.displayName = name;
        weapon.damage = attackDamage;
        weapon.damageType = type;
        weapon.onHitStatuses = statuses ?? new StatusEffectDefinition[0];
        weapon.attackCooldown = cooldown;
        weapon.attackRange = range;
        weapon.windupSeconds = windup;
        weapon.contactSeconds = contact;
        weapon.recoverySeconds = recovery;
        weapon.windupCancelledByMove = cancelWindupByMove;
        weapon.recoveryCancellable = cancellableRecovery;
        weapon.hitDistance = distance;
        weapon.hitOffsets = NormalizeDirectionalOffsets(offsets);
        weapon.hitSize = size ?? new Vector2(0.9f, 0.9f);
        weapon.attackClip = attackClipName;
        if (projectile.HasValue)
        {
            ProjectileSpec spec = projectile.Value;
            weapon.delivery = AttackDelivery.Projectile;
            weapon.projectileSpeed = spec.Speed;
            weapon.projectileRange = spec.Range;
            weapon.projectileRadius = spec.Radius;
            weapon.projectileLaunchDistance = spec.LaunchDistance;
            weapon.projectileHoming = spec.Homing;
            weapon.projectileVisual = spec.Visual;
        }

        weapon.hudIcon = icon;
        weapon.abilities = AbilitySlotArrays.Resize(weaponAbilities, AbilityCount);
        weapon.hideFlags = HideFlags.HideAndDontSave;
        return weapon;
    }

    /// <summary>
    /// Replaces the hit offsets with a copy holding exactly four entries.
    /// </summary>
    private void EnsureDirectionalOffsets()
    {
        if (hitOffsets == null || hitOffsets.Length != DirectionalOffsetCount)
        {
            hitOffsets = NormalizeDirectionalOffsets(hitOffsets);
        }
    }

    /// <summary>
    /// A new four-entry array (Down, Up, Left, Right) copied from <paramref name="offsets"/>, truncated or padded with
    /// zero offsets; all zero when it is null.
    /// </summary>
    private static Vector2[] NormalizeDirectionalOffsets(Vector2[] offsets)
    {
        Vector2[] normalizedOffsets = new Vector2[DirectionalOffsetCount];
        if (offsets == null)
        {
            return normalizedOffsets;
        }

        int copyCount = Mathf.Min(offsets.Length, DirectionalOffsetCount);
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
