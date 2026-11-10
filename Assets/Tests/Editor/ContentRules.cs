using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The content rules (Docs/DesignSystem.md, Mechanics) as checks on a definition's serialized values, not on the
/// getters that clamp them: references set, ranges valid, names present and unique, colours opaque, catalogs listing
/// each definition once. Each check returns a line per problem naming the definition and the field; none when it
/// follows the rules. A field it reads that no longer exists throws, so a renamed field fails loudly.
/// </summary>
internal static class ContentRules
{
    /// <summary>
    /// A weapon: its id and name, a hit that is not negative, a cooldown and contact above 0, the windup, recovery and
    /// range not negative, no empty status entry, a HUD icon and an attack clip, and an area (melee) or a projectile
    /// with its visual.
    /// </summary>
    public static List<string> Weapon(PlayerWeapon weapon)
    {
        List<string> problems = new();
        SerializedObject data = new(weapon);
        string owner = weapon.name;
        NotEmpty(problems, data, owner, "weaponId");
        NotEmpty(problems, data, owner, "displayName");
        AtLeast(problems, data, owner, "damage", 0);
        Above(problems, data, owner, "attackCooldown", 0);
        AtLeast(problems, data, owner, "attackRange", 0);
        AtLeast(problems, data, owner, "windupSeconds", 0);
        Above(problems, data, owner, "contactSeconds", 0);
        AtLeast(problems, data, owner, "recoverySeconds", 0);
        NoEmptyEntries(problems, data, owner, "onHitStatuses");
        Set(problems, data, owner, "hudIcon");
        NotEmpty(problems, data, owner, "attackClip");
        if (Property(data, "delivery").intValue == (int)AttackDelivery.Projectile)
        {
            Above(problems, data, owner, "projectileSpeed", 0);
            Above(problems, data, owner, "projectileRange", 0);
            Above(problems, data, owner, "projectileRadius", 0);
            Set(problems, data, owner, "projectileVisual");
        }
        else
        {
            Vector2 size = Property(data, "hitSize").vector2Value;
            if (size.x <= 0f || size.y <= 0f)
            {
                problems.Add($"{owner}: hitSize {size} is not above 0 each way.");
            }
        }

        return problems;
    }

    /// <summary>
    /// An ability: a name, a cooldown above 0, a range above 0 when aimed at a point or a unit, a cast time, range,
    /// area, heal and hit that are not negative, no empty status entry, and a release above 0.
    /// </summary>
    public static List<string> Ability(AbilityDefinition ability)
    {
        List<string> problems = new();
        SerializedObject data = new(ability);
        string owner = ability.name;
        NotEmpty(problems, data, owner, "displayName");
        Above(problems, data, owner, "cooldown", 0);
        AtLeast(problems, data, owner, "castTime", 0);
        AbilityTargeting targeting = (AbilityTargeting)Property(data, "targeting").intValue;
        if (targeting == AbilityTargeting.Point || targeting == AbilityTargeting.Unit)
        {
            Above(problems, data, owner, "range", 0);
        }
        else
        {
            AtLeast(problems, data, owner, "range", 0);
        }

        AtLeast(problems, data, owner, "areaRadius", 0);
        AtLeast(problems, data, owner, "heal", 0);
        AtLeast(problems, data, owner, "hit.amount", 0);
        NoEmptyEntries(problems, data, owner, "hit.statuses");
        Above(problems, data, owner, "releaseSeconds", 0);
        AtLeast(problems, data, owner, "followThroughSeconds", 0);
        AtLeast(problems, data, owner, "channelBeat", 0);
        return problems;
    }

    /// <summary>
    /// A class: exactly <see cref="PlayerClass.AbilityCount"/> slots, which may be empty.
    /// </summary>
    public static List<string> Class(PlayerClass playerClass)
    {
        List<string> problems = new();
        int slots = Property(new SerializedObject(playerClass), "abilities").arraySize;
        if (slots != PlayerClass.AbilityCount)
        {
            problems.Add($"{playerClass.name}: abilities has {slots} slots, not {PlayerClass.AbilityCount}.");
        }

        return problems;
    }

    /// <summary>
    /// A status: a name and a description, an opaque colour, a duration above 0, at least one stack, a periodic
    /// effect with an amount and an interval above 0, multipliers that are not negative and resistance deltas within
    /// ±100.
    /// </summary>
    public static List<string> Status(StatusEffectDefinition status)
    {
        List<string> problems = new();
        SerializedObject data = new(status);
        string owner = status.name;
        NotEmpty(problems, data, owner, "displayName");
        NotEmpty(problems, data, owner, "description");
        Opaque(problems, data, owner, "color");
        Above(problems, data, owner, "duration", 0);
        AtLeast(problems, data, owner, "maxStacks", 1);
        if (Property(data, "periodic").intValue != (int)StatusPeriodic.None)
        {
            Above(problems, data, owner, "periodicAmount", 0);
            Above(problems, data, owner, "tickInterval", 0);
        }

        AtLeast(problems, data, owner, "moveSpeedMultiplier", 0);
        AtLeast(problems, data, owner, "damageDealtMultiplier", 0);
        AtLeast(problems, data, owner, "damageTakenMultiplier", 0);
        Percents(problems, data, owner, "resistanceDeltas");
        return problems;
    }

    /// <summary>
    /// A combat profile: a faction, and resistances within ±100.
    /// </summary>
    public static List<string> Profile(CombatProfile profile)
    {
        List<string> problems = new();
        SerializedObject data = new(profile);
        if (Property(data, "faction").intValue == (int)Faction.None)
        {
            problems.Add($"{profile.name}: faction is None.");
        }

        Percents(problems, data, profile.name, "resistances");
        return problems;
    }

    /// <summary>
    /// A mob config: a move speed and detection radius above 0, an attack that is not negative with a cooldown and
    /// contact above 0, an area above 0 each way, an attack clip, no empty status entry, and an obstacle mask.
    /// </summary>
    public static List<string> Mob(MobConfig mob)
    {
        List<string> problems = new();
        SerializedObject data = new(mob);
        string owner = mob.name;
        Above(problems, data, owner, "moveSpeed", 0);
        Above(problems, data, owner, "detectionRadius", 0);
        AtLeast(problems, data, owner, "attackDamage", 0);
        Above(problems, data, owner, "attackInterval", 0);
        AtLeast(problems, data, owner, "attackWindup", 0);
        Above(problems, data, owner, "attackContact", 0);
        AtLeast(problems, data, owner, "attackRecovery", 0);
        NotEmpty(problems, data, owner, "attackClip");
        NoEmptyEntries(problems, data, owner, "attackStatuses");
        Vector2 area = Property(data, "attackArea").vector2Value;
        if (area.x <= 0f || area.y <= 0f)
        {
            problems.Add($"{owner}: attackArea {area} is not above 0 each way.");
        }

        if (Property(data, "obstacleLayerMask").intValue == 0)
        {
            problems.Add($"{owner}: obstacleLayerMask is empty.");
        }

        return problems;
    }

    /// <summary>
    /// The combat settings: the resistance floor from -100 to 0 and the cap from 0 to 99, a minimum damage that is not
    /// negative, the periodic popup scale from 0.3 to 1, a status catalog, an immune text and opaque popup colours.
    /// </summary>
    public static List<string> Combat(CombatSettings settings)
    {
        List<string> problems = new();
        SerializedObject data = new(settings);
        string owner = settings.name;
        Within(problems, data, owner, "resistanceFloor", -100, 0);
        Within(problems, data, owner, "resistanceCap", 0, 99);
        AtLeast(problems, data, owner, "minimumDamage", 0);
        Within(problems, data, owner, "periodicPopupScale", 0.3, 1);
        Set(problems, data, owner, "statusCatalog");
        NotEmpty(problems, data, owner, "immuneText");
        foreach (string colour in new[] { "physicalColor", "fireColor", "frostColor", "lightningColor", "poisonColor", "trueColor", "healColor", "immuneColor" })
        {
            Opaque(problems, data, owner, colour);
        }

        return problems;
    }

    /// <summary>
    /// A look catalog: each look with a name, an animation set and locomotion settings, and the names unique.
    /// </summary>
    public static List<string> Looks(CharacterLookCatalog catalog)
    {
        List<string> problems = new();
        SerializedProperty looks = Property(new SerializedObject(catalog), "looks");
        List<string> names = new();
        for (int i = 0; i < looks.arraySize; i++)
        {
            SerializedProperty look = looks.GetArrayElementAtIndex(i);
            string owner = $"{catalog.name} look {i}";
            string lookName = Relative(look, "name").stringValue;
            if (string.IsNullOrWhiteSpace(lookName))
            {
                problems.Add($"{owner}: name is empty.");
            }

            names.Add(lookName);
            if (Relative(look, "animationSet").objectReferenceValue == null)
            {
                problems.Add($"{owner}: animationSet is not set.");
            }

            if (Relative(look, "locomotion").objectReferenceValue == null)
            {
                problems.Add($"{owner}: locomotion is not set.");
            }
        }

        problems.AddRange(Unique($"{catalog.name} look names", names));
        return problems;
    }

    /// <summary>
    /// The character colours: list names unique, swatch names unique within their list and every ramp opaque, choice
    /// names unique, and each choice's list and own swatch existing.
    /// </summary>
    public static List<string> Colours(CharacterColours colours)
    {
        List<string> problems = new();
        SerializedObject data = new(colours);
        SerializedProperty lists = Property(data, "lists");
        Dictionary<string, List<string>> swatchesByList = new();
        for (int l = 0; l < lists.arraySize; l++)
        {
            SerializedProperty list = lists.GetArrayElementAtIndex(l);
            string listName = Relative(list, "name").stringValue;
            List<string> swatchNames = new();
            SerializedProperty swatches = Relative(list, "swatches");
            for (int s = 0; s < swatches.arraySize; s++)
            {
                SerializedProperty swatch = swatches.GetArrayElementAtIndex(s);
                string swatchName = Relative(swatch, "name").stringValue;
                swatchNames.Add(swatchName);
                SerializedProperty ramps = Relative(swatch, "ramps");
                for (int r = 0; r < ramps.arraySize; r++)
                {
                    SerializedProperty ramp = ramps.GetArrayElementAtIndex(r);
                    foreach (string tone in new[] { "outline", "shadow", "middle", "light" })
                    {
                        if (Relative(ramp, tone).colorValue.a < 1f)
                        {
                            problems.Add($"{colours.name}: {listName} {swatchName} ramp {r} {tone} is not opaque.");
                        }
                    }
                }
            }

            problems.AddRange(Unique($"{colours.name} {listName} swatch names", swatchNames));
            if (!swatchesByList.ContainsKey(listName))
            {
                swatchesByList.Add(listName, swatchNames);
            }
        }

        List<string> listNames = new();
        for (int l = 0; l < lists.arraySize; l++)
        {
            listNames.Add(Relative(lists.GetArrayElementAtIndex(l), "name").stringValue);
        }

        problems.AddRange(Unique($"{colours.name} list names", listNames));

        SerializedProperty choices = Property(data, "choices");
        List<string> choiceNames = new();
        for (int c = 0; c < choices.arraySize; c++)
        {
            SerializedProperty choice = choices.GetArrayElementAtIndex(c);
            string choiceName = Relative(choice, "name").stringValue;
            choiceNames.Add(choiceName);
            string listName = Relative(choice, "list").stringValue;
            if (!swatchesByList.TryGetValue(listName, out List<string> swatchNames))
            {
                problems.Add($"{colours.name}: choice {choiceName} picks from {listName}, which is not a list.");
            }
            else if (!swatchNames.Contains(Relative(choice, "own").stringValue))
            {
                problems.Add($"{colours.name}: choice {choiceName}'s own swatch {Relative(choice, "own").stringValue} is not in {listName}.");
            }
        }

        problems.AddRange(Unique($"{colours.name} choice names", choiceNames));
        return problems;
    }

    /// <summary>
    /// A catalog: no empty entry, each definition listed once, and every definition <paramref name="found"/> listed.
    /// </summary>
    public static List<string> Catalog(string catalog, IReadOnlyList<Object> listed, IEnumerable<Object> found)
    {
        List<string> problems = new();
        HashSet<Object> seen = new();
        for (int i = 0; i < listed.Count; i++)
        {
            if (listed[i] == null)
            {
                problems.Add($"{catalog}: entry {i} is empty.");
            }
            else if (!seen.Add(listed[i]))
            {
                problems.Add($"{catalog}: {listed[i].name} is listed twice.");
            }
        }

        foreach (Object definition in found)
        {
            if (!seen.Contains(definition))
            {
                problems.Add($"{catalog}: {definition.name} is not listed.");
            }
        }

        return problems;
    }

    /// <summary>
    /// A problem for each name that appears more than once in <paramref name="names"/>.
    /// </summary>
    public static List<string> Unique(string what, IEnumerable<string> names)
    {
        List<string> problems = new();
        HashSet<string> seen = new();
        HashSet<string> reported = new();
        foreach (string name in names)
        {
            if (!seen.Add(name) && reported.Add(name))
            {
                problems.Add($"{what}: {name} is used more than once.");
            }
        }

        return problems;
    }

    /// <summary>
    /// The property at <paramref name="path"/>; throws when the definition has no such field.
    /// </summary>
    private static SerializedProperty Property(SerializedObject data, string path)
    {
        SerializedProperty property = data.FindProperty(path);
        if (property == null)
        {
            throw new InvalidOperationException($"{data.targetObject.GetType().Name} has no serialized field {path}.");
        }

        return property;
    }

    /// <summary>
    /// The property at <paramref name="path"/> under <paramref name="parent"/>; throws when there is none.
    /// </summary>
    private static SerializedProperty Relative(SerializedProperty parent, string path)
    {
        SerializedProperty property = parent.FindPropertyRelative(path);
        if (property == null)
        {
            throw new InvalidOperationException($"{parent.propertyPath} has no serialized field {path}.");
        }

        return property;
    }

    /// <summary>
    /// A numeric property's value, integer or float.
    /// </summary>
    private static double Number(SerializedProperty property)
    {
        return property.propertyType == SerializedPropertyType.Integer ? property.intValue : property.floatValue;
    }

    /// <summary>
    /// Reports a number at or below <paramref name="floor"/>.
    /// </summary>
    private static void Above(List<string> problems, SerializedObject data, string owner, string path, double floor)
    {
        double value = Number(Property(data, path));
        if (value <= floor)
        {
            problems.Add($"{owner}: {path} is {value}, not above {floor}.");
        }
    }

    /// <summary>
    /// Reports a number below <paramref name="floor"/>.
    /// </summary>
    private static void AtLeast(List<string> problems, SerializedObject data, string owner, string path, double floor)
    {
        double value = Number(Property(data, path));
        if (value < floor)
        {
            problems.Add($"{owner}: {path} is {value}, below {floor}.");
        }
    }

    /// <summary>
    /// Reports a number outside <paramref name="low"/> to <paramref name="high"/>.
    /// </summary>
    private static void Within(List<string> problems, SerializedObject data, string owner, string path, double low, double high)
    {
        double value = Number(Property(data, path));
        if (value < low || value > high)
        {
            problems.Add($"{owner}: {path} is {value}, outside {low} to {high}.");
        }
    }

    /// <summary>
    /// Reports an empty or blank string.
    /// </summary>
    private static void NotEmpty(List<string> problems, SerializedObject data, string owner, string path)
    {
        if (string.IsNullOrWhiteSpace(Property(data, path).stringValue))
        {
            problems.Add($"{owner}: {path} is empty.");
        }
    }

    /// <summary>
    /// Reports an object reference that is not set.
    /// </summary>
    private static void Set(List<string> problems, SerializedObject data, string owner, string path)
    {
        if (Property(data, path).objectReferenceValue == null)
        {
            problems.Add($"{owner}: {path} is not set.");
        }
    }

    /// <summary>
    /// Reports each empty entry of an array of object references.
    /// </summary>
    private static void NoEmptyEntries(List<string> problems, SerializedObject data, string owner, string path)
    {
        SerializedProperty array = Property(data, path);
        for (int i = 0; i < array.arraySize; i++)
        {
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == null)
            {
                problems.Add($"{owner}: {path}[{i}] is empty.");
            }
        }
    }

    /// <summary>
    /// Reports a colour that is not fully opaque.
    /// </summary>
    private static void Opaque(List<string> problems, SerializedObject data, string owner, string path)
    {
        if (Property(data, path).colorValue.a < 1f)
        {
            problems.Add($"{owner}: {path} is not opaque.");
        }
    }

    /// <summary>
    /// Reports each resistance of an array of <see cref="DamageResistance"/> outside ±100.
    /// </summary>
    private static void Percents(List<string> problems, SerializedObject data, string owner, string path)
    {
        SerializedProperty array = Property(data, path);
        for (int i = 0; i < array.arraySize; i++)
        {
            int percent = Relative(array.GetArrayElementAtIndex(i), "percent").intValue;
            if (percent < -100 || percent > 100)
            {
                problems.Add($"{owner}: {path}[{i}] is {percent}%, outside ±100.");
            }
        }
    }
}
