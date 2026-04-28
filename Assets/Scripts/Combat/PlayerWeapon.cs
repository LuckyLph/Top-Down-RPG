using UnityEngine;

[CreateAssetMenu(fileName = "Player Weapon", menuName = "TopDownRPG/Combat/Player Weapon")]
public sealed class PlayerWeapon : ScriptableObject
{
    [SerializeField] private string displayName = "Sword";
    [SerializeField, Min(0)] private int damage = 1;
    [SerializeField, Min(0.01f)] private float attackCooldown = 0.35f;
    [SerializeField, Min(0.01f)] private float slashDuration = 0.18f;
    [SerializeField, Min(0f)] private float slashSpawnDistance = 0.55f;
    [SerializeField] private Vector2 slashSpawnOffset = Vector2.zero;
    [SerializeField] private PlayerSlashAttack slashPrefab;
    [SerializeField] private AnimationClip slashAnimation;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Weapon" : displayName;
    public int Damage => Mathf.Max(0, damage);
    public float AttackCooldown => Mathf.Max(0.01f, attackCooldown);
    public float SlashDuration => Mathf.Max(0.01f, slashDuration);
    public float SlashSpawnDistance => Mathf.Max(0f, slashSpawnDistance);
    public Vector2 SlashSpawnOffset => slashSpawnOffset;
    public PlayerSlashAttack SlashPrefab => slashPrefab;
    public AnimationClip SlashAnimation => slashAnimation;

    public static PlayerWeapon Create(
        string name,
        int attackDamage,
        float cooldown,
        float duration,
        float spawnDistance,
        Vector2 spawnOffset,
        PlayerSlashAttack prefab = null,
        AnimationClip animation = null)
    {
        PlayerWeapon weapon = CreateInstance<PlayerWeapon>();
        weapon.displayName = name;
        weapon.damage = attackDamage;
        weapon.attackCooldown = cooldown;
        weapon.slashDuration = duration;
        weapon.slashSpawnDistance = spawnDistance;
        weapon.slashSpawnOffset = spawnOffset;
        weapon.slashPrefab = prefab;
        weapon.slashAnimation = animation;
        weapon.hideFlags = HideFlags.HideAndDontSave;
        return weapon;
    }
}
