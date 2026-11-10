using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What the design tables are built from (Docs/DesignSystem.md, Generated tables): the combat settings with their
/// status catalog, the weapon catalog, the abilities, the classes and the units. <see cref="FromProject"/> reads the
/// project's; tests build their own from <c>Create</c>.
/// </summary>
public sealed class DesignContent
{
    public const string CombatSettingsPath = "Assets/Data/Combat/CombatSettings.asset";
    public const string WeaponCatalogPath = "Assets/Data/Weapons/WeaponCatalog.asset";
    public const string DataFolder = "Assets/Data";

    public static readonly string[] UnitFolders = { "Assets/Prefabs/Player", "Assets/Prefabs/Mobs" };

    /// <summary>
    /// Creates the content from its parts; the abilities are sorted by name, the units players first, then by name.
    /// </summary>
    public DesignContent(
        CombatSettings combat,
        WeaponCatalog weapons,
        IEnumerable<AbilityDefinition> abilities,
        IEnumerable<PlayerClass> classes,
        IEnumerable<DesignUnit> units)
    {
        Combat = combat;
        Weapons = weapons;

        List<AbilityDefinition> sortedAbilities = new(abilities);
        sortedAbilities.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        Abilities = sortedAbilities;

        List<PlayerClass> sortedClasses = new(classes);
        sortedClasses.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        Classes = sortedClasses;

        List<DesignUnit> sortedUnits = new(units);
        sortedUnits.Sort((a, b) => a.IsMob != b.IsMob ? a.IsMob.CompareTo(b.IsMob) : string.CompareOrdinal(a.Name, b.Name));
        Units = sortedUnits;
    }

    public CombatSettings Combat { get; }
    public StatusEffectCatalog Statuses => Combat != null ? Combat.StatusCatalog : null;
    public WeaponCatalog Weapons { get; }
    public IReadOnlyList<AbilityDefinition> Abilities { get; }
    public IReadOnlyList<PlayerClass> Classes { get; }
    public IReadOnlyList<DesignUnit> Units { get; }

    /// <summary>
    /// The project's content: the combat settings and weapon catalog at their paths, every ability and class under
    /// <see cref="DataFolder"/>, and every unit prefab in <see cref="UnitFolders"/>. Throws when the settings or the
    /// catalog is missing.
    /// </summary>
    public static DesignContent FromProject()
    {
        CombatSettings combat = AssetDatabase.LoadAssetAtPath<CombatSettings>(CombatSettingsPath);
        WeaponCatalog weapons = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponCatalogPath);
        if (combat == null || weapons == null)
        {
            throw new InvalidOperationException($"The design tables need {CombatSettingsPath} and {WeaponCatalogPath}.");
        }

        List<DesignUnit> units = new();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", UnitFolders))
        {
            DesignUnit unit = DesignUnit.FromPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
            if (unit != null)
            {
                units.Add(unit);
            }
        }

        return new DesignContent(combat, weapons, Load<AbilityDefinition>(), Load<PlayerClass>(), units);
    }

    /// <summary>
    /// Every asset of type <typeparamref name="T"/> under <see cref="DataFolder"/>.
    /// </summary>
    private static List<T> Load<T>() where T : ScriptableObject
    {
        List<T> assets = new();
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { DataFolder }))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null)
            {
                assets.Add(asset);
            }
        }

        return assets;
    }
}
