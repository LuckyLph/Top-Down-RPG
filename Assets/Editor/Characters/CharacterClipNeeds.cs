using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The clips a character's prefab plays, which its animation set must hold (Docs/AsepriteCharacters.md, Validation): a
/// player's class abilities' cast clips (<see cref="PlayerAbilities"/>), and for every weapon of its catalog
/// (<see cref="PlayerWeaponController"/>) its attack clip and its abilities' cast clips; a mob's attack clip
/// (<see cref="MobController"/>). A Continue cast's clip over the run is not needed: without it the cast plays standing.
/// </summary>
public static class CharacterClipNeeds
{
    /// <summary>
    /// Each clip <paramref name="character"/> plays with what plays it, in the order above, an empty clip name skipped;
    /// none for no prefab.
    /// </summary>
    public static IReadOnlyList<(string Clip, string Need)> Of(GameObject character)
    {
        List<(string, string)> needs = new();
        if (character == null)
        {
            return needs;
        }

        if (character.TryGetComponent(out PlayerAbilities abilities) && abilities.Class != null)
        {
            for (int slot = 0; slot < PlayerClass.AbilityCount; slot++)
            {
                AddCast(needs, abilities.Class.GetAbility(slot), $"the class {abilities.Class.name}");
            }
        }

        if (character.TryGetComponent(out PlayerWeaponController weapons) && weapons.Catalog != null)
        {
            for (int i = 0; i < weapons.Catalog.Count; i++)
            {
                PlayerWeapon weapon = weapons.Catalog.Get(i);
                if (weapon == null)
                {
                    continue;
                }

                Add(needs, weapon.AttackClip, $"the weapon {weapon.name}'s attack");
                for (int slot = 0; slot < PlayerWeapon.AbilityCount; slot++)
                {
                    AddCast(needs, weapon.GetAbility(slot), $"the weapon {weapon.name}");
                }
            }
        }

        if (character.TryGetComponent(out MobController mob) && mob.Config != null)
        {
            Add(needs, mob.Config.AttackClip, $"the mob {mob.Config.name}'s attack");
        }

        return needs;
    }

    /// <summary>
    /// One line per clip <paramref name="character"/> plays that <paramref name="clips"/> lacks, naming what plays it.
    /// </summary>
    public static IReadOnlyList<string> Missing(GameObject character, IEnumerable<string> clips)
    {
        HashSet<string> held = new(clips);
        return Of(character).Where(n => !held.Contains(n.Clip)).Select(n => $"{character.name} plays {n.Clip} for {n.Need}, and the set has no such clip.").ToList();
    }

    /// <summary>
    /// Adds <paramref name="ability"/>'s cast clip, cast for <paramref name="owner"/>, when it has one.
    /// </summary>
    private static void AddCast(List<(string, string)> needs, AbilityDefinition ability, string owner)
    {
        if (ability != null)
        {
            Add(needs, ability.CastClip, $"{owner}'s ability {ability.name}");
        }
    }

    /// <summary>
    /// Adds <paramref name="clip"/>, played for <paramref name="need"/>, unless it is empty.
    /// </summary>
    private static void Add(List<(string, string)> needs, string clip, string need)
    {
        if (!string.IsNullOrEmpty(clip))
        {
            needs.Add((clip, need));
        }
    }
}
