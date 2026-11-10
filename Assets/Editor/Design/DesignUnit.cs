using UnityEngine;

/// <summary>
/// A unit as the design tables read it (Docs/DesignSystem.md, Generated tables): its name, health, combat profile and
/// move speed, and either the mob config its attack comes from or the weapon a player starts with.
/// </summary>
public sealed class DesignUnit
{
    /// <summary>
    /// Creates a unit; a mob has <paramref name="mob"/>, a player has none and may have a
    /// <paramref name="startingWeapon"/>.
    /// </summary>
    public DesignUnit(string name, int health, CombatProfile profile, float moveSpeed, MobConfig mob, PlayerWeapon startingWeapon)
    {
        Name = name;
        Health = health;
        Profile = profile;
        MoveSpeed = moveSpeed;
        Mob = mob;
        StartingWeapon = startingWeapon;
    }

    public string Name { get; }
    public int Health { get; }
    public CombatProfile Profile { get; }
    public float MoveSpeed { get; }
    public MobConfig Mob { get; }
    public PlayerWeapon StartingWeapon { get; }
    public bool IsMob => Mob != null;
    public Faction Faction => Profile != null ? Profile.Faction : Faction.None;

    /// <summary>
    /// The unit a prefab is: a root with a <see cref="Health"/> and a <see cref="DamageReceiver"/>, and a
    /// <see cref="MobController"/> with its config or a <see cref="PlayerController"/>. Null for any other prefab.
    /// </summary>
    public static DesignUnit FromPrefab(GameObject prefab)
    {
        if (prefab == null || !prefab.TryGetComponent(out Health health) || !prefab.TryGetComponent(out DamageReceiver receiver))
        {
            return null;
        }

        if (prefab.TryGetComponent(out MobController mob) && mob.Config != null)
        {
            return new DesignUnit(prefab.name, health.MaxHealth, receiver.Profile, mob.Config.MoveSpeed, mob.Config, null);
        }

        if (prefab.TryGetComponent(out PlayerController player))
        {
            float speed = player.ControlSettings != null ? player.ControlSettings.MoveSpeed : 0f;
            PlayerWeapon weapon = prefab.TryGetComponent(out PlayerWeaponController weapons) ? weapons.StartingWeapon : null;
            return new DesignUnit(prefab.name, health.MaxHealth, receiver.Profile, speed, null, weapon);
        }

        return null;
    }
}
