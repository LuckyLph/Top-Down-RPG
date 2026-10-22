using UnityEngine;

/// <summary>
/// Every weapon a player can equip, in a fixed order (Docs/Animation.md, Phase 4). A weapon is identified on the wire by
/// its index here (the equipped weapon, and the weapon an attack or a weapon ability was made with), which is safe
/// because join approval refuses clients on another build version. Append weapons; never reorder them.
/// </summary>
[CreateAssetMenu(fileName = "WeaponCatalog", menuName = "TopDownRPG/Combat/Weapon Catalog")]
public sealed class WeaponCatalog : ScriptableObject
{
    [SerializeField] private PlayerWeapon[] weapons = new PlayerWeapon[0];

    public int Count => weapons != null ? weapons.Length : 0;

    /// <summary>
    /// The weapon at <paramref name="index"/>, or null when the index is out of range.
    /// </summary>
    public PlayerWeapon Get(int index)
    {
        return index >= 0 && index < Count ? weapons[index] : null;
    }

    /// <summary>
    /// The index of <paramref name="weapon"/>, or -1 when it is not in the catalog.
    /// </summary>
    public int IndexOf(PlayerWeapon weapon)
    {
        if (weapon == null)
        {
            return -1;
        }

        for (int i = 0; i < Count; i++)
        {
            if (weapons[i] == weapon)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The weapon after <paramref name="weapon"/>, wrapping around; the first one when it is not in the catalog, and
    /// null for an empty catalog.
    /// </summary>
    public PlayerWeapon Next(PlayerWeapon weapon)
    {
        return Count == 0 ? null : Get((IndexOf(weapon) + 1) % Count);
    }

    /// <summary>
    /// Creates an unsaved, hidden catalog of <paramref name="catalogWeapons"/> in that order, for tests.
    /// </summary>
    public static WeaponCatalog Create(params PlayerWeapon[] catalogWeapons)
    {
        WeaponCatalog catalog = CreateInstance<WeaponCatalog>();
        catalog.weapons = catalogWeapons ?? new PlayerWeapon[0];
        catalog.hideFlags = HideFlags.HideAndDontSave;
        return catalog;
    }
}
