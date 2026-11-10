using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the content rules (Docs/DesignSystem.md, Phase 2): every definition of each kind in the project follows
/// <see cref="ContentRules"/>, read from its serialized values and never written; and each rule reports a definition
/// built with <c>Create</c> that breaks it.
/// </summary>
public class ContentTests
{
    private static readonly string[] DataFolders = { "Assets/Data" };
    private static readonly string[] MobFolders = { "Assets/Data", "Assets/Settings/AI" };

    private static readonly string[] Kinds =
    {
        "Weapons", "Abilities", "Classes", "Statuses", "Combat profiles", "Mob configs", "Looks", "Character colours", "Combat settings",
    };

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every asset instance the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Every definition of <paramref name="kind"/> in the project follows the content rules, and the kind has at least
    /// one, so a moved folder cannot leave the check with nothing to read.
    /// </summary>
    [TestCaseSource(nameof(Kinds))]
    public void EveryDefinition_FollowsTheContentRules(string kind)
    {
        List<string> problems = Check(kind);
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// A weapon from <c>Create</c> with an icon follows the rules; one with a negative hit, a zero cooldown and contact,
    /// an empty status entry and no icon is reported for each.
    /// </summary>
    [Test]
    public void AWeapon_BreakingTheRules_IsReported()
    {
        PlayerWeapon good = Track(PlayerWeapon.Create("Good", 10, 0.5f, 0.65f, null, Icon()));
        PlayerWeapon bad = Track(PlayerWeapon.Create("Bad", -1, 0f, 0.65f, null, contact: 0f, statuses: new StatusEffectDefinition[] { null }));

        Assert.That(ContentRules.Weapon(good), Is.Empty);
        AssertReports(ContentRules.Weapon(bad), "damage", "attackCooldown", "contactSeconds", "onHitStatuses[0]", "hudIcon");
    }

    /// <summary>
    /// An ability from <c>Create</c> follows the rules; one with no cooldown, aimed at a point with no range, is
    /// reported for both.
    /// </summary>
    [Test]
    public void AnAbility_BreakingTheRules_IsReported()
    {
        AbilityDefinition good = Track(AbilityDefinition.Create("Good", 2f, targeting: AbilityTargeting.Point, range: 4f));
        AbilityDefinition bad = Track(AbilityDefinition.Create("Bad", 0f, targeting: AbilityTargeting.Point, range: 0f));

        Assert.That(ContentRules.Ability(good), Is.Empty);
        AssertReports(ContentRules.Ability(bad), "cooldown", "range");
    }

    /// <summary>
    /// A status with a description follows the rules; one with no duration, no stack, a periodic effect of 0, a
    /// see-through colour and no description is reported for each; two whose names start alike share an abbreviation.
    /// </summary>
    [Test]
    public void AStatus_BreakingTheRules_IsReported()
    {
        StatusEffectDefinition good = Track(StatusEffectDefinition.Create("Stun", StatusKind.Debuff, 2f));
        Describe(good, "Cannot move.");
        StatusEffectDefinition bad = Track(StatusEffectDefinition.Create(
            "Stone", StatusKind.Debuff, 0f, maxStacks: 0, periodic: StatusPeriodic.Damage, periodicAmount: 0, color: new Color(1f, 1f, 1f, 0.5f)));

        Assert.That(ContentRules.Status(good), Is.Empty);
        AssertReports(ContentRules.Status(bad), "duration", "maxStacks", "periodicAmount", "color", "description");
        AssertReports(ContentRules.Unique("abbreviations", new[] { good.Abbreviation, bad.Abbreviation }), "St");
    }

    /// <summary>
    /// A profile with a faction follows the rules; one with no faction and a resistance past 100% is reported for both.
    /// </summary>
    [Test]
    public void AProfile_BreakingTheRules_IsReported()
    {
        CombatProfile good = Track(CombatProfile.Create(Faction.Mobs, resistances: new DamageResistance(DamageType.Fire, -50)));
        CombatProfile bad = Track(CombatProfile.Create(Faction.None, resistances: new DamageResistance(DamageType.Fire, 150)));

        Assert.That(ContentRules.Profile(good), Is.Empty);
        AssertReports(ContentRules.Profile(bad), "faction", "resistances[0]");
    }

    /// <summary>
    /// A mob config with an attack clip follows the rules; one without, with no cooldown and no contact, is reported for
    /// each.
    /// </summary>
    [Test]
    public void AMobConfig_BreakingTheRules_IsReported()
    {
        MobConfig good = Track(MobConfig.Create(attackClip: "Bite"));
        MobConfig bad = Track(MobConfig.Create(attackInterval: 0f, attackContact: 0f));

        Assert.That(ContentRules.Mob(good), Is.Empty);
        AssertReports(ContentRules.Mob(bad), "attackInterval", "attackContact", "attackClip");
    }

    /// <summary>
    /// The combat settings from <c>Create</c> follow the rules; a cap past 99 and a see-through popup colour are
    /// reported.
    /// </summary>
    [Test]
    public void CombatSettings_BreakingTheRules_AreReported()
    {
        CombatSettings good = Track(CombatSettings.Create(Track(StatusEffectCatalog.Create())));
        CombatSettings bad = Track(CombatSettings.Create(Track(StatusEffectCatalog.Create())));
        SerializedObject data = new(bad);
        data.FindProperty("resistanceCap").intValue = 120;
        data.FindProperty("fireColor").colorValue = new Color(1f, 0.5f, 0f, 0.5f);
        data.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(ContentRules.Combat(good), Is.Empty);
        AssertReports(ContentRules.Combat(bad), "resistanceCap", "fireColor");
    }

    /// <summary>
    /// A catalog listing a definition twice, with an empty entry and without another definition is reported for each;
    /// a class without four slots and a look without its set and locomotion are reported too.
    /// </summary>
    [Test]
    public void CatalogsClassesAndLooks_BreakingTheRules_AreReported()
    {
        StatusEffectDefinition burn = Track(StatusEffectDefinition.Create("Burn", StatusKind.Debuff, 4f));
        StatusEffectDefinition chill = Track(StatusEffectDefinition.Create("Chill", StatusKind.Debuff, 3f));
        PlayerClass playerClass = Track(PlayerClass.Create("Class"));
        typeof(PlayerClass).GetField("abilities", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(playerClass, new AbilityDefinition[3]);
        CharacterLookCatalog looks = Track(CharacterLookCatalog.Create(new CharacterLook("Look", null, null), new CharacterLook("Look", null, null)));

        AssertReports(ContentRules.Catalog("catalog", new Object[] { burn, burn, null }, new Object[] { burn, chill }), "listed twice", "entry 2 is empty", "Chill is not listed");
        AssertReports(ContentRules.Class(playerClass), "3 slots");
        AssertReports(ContentRules.Looks(looks), "animationSet", "locomotion", "Look is used more than once");
    }

    /// <summary>
    /// Character colours with a see-through ramp, a choice picking from a list that does not exist and two lists of
    /// one name are reported for each.
    /// </summary>
    [Test]
    public void CharacterColours_BreakingTheRules_AreReported()
    {
        Color32 solid = new(10, 20, 30, 255);
        ColourRamp clear = new(solid, solid, solid, new Color32(10, 20, 30, 0));
        CharacterColours colours = Track(CharacterColours.Create(
            new[]
            {
                new CharacterSwatchList("Skin", new[] { new CharacterSwatch("Pale", clear) }),
                new CharacterSwatchList("Skin", new[] { new CharacterSwatch("Dark", new ColourRamp(solid, solid, solid, solid)) }),
            },
            new[] { new CharacterColourChoice("Hair", "Hair", "Brown") }));

        AssertReports(ContentRules.Colours(colours), "Pale ramp 0 light", "Hair, which is not a list", "list names: Skin");
    }

    /// <summary>
    /// The problems with the project's definitions of <paramref name="kind"/>.
    /// </summary>
    private static List<string> Check(string kind)
    {
        List<string> problems = new();
        switch (kind)
        {
            case "Weapons":
            {
                List<PlayerWeapon> weapons = Find<PlayerWeapon>(DataFolders, problems, kind);
                weapons.ForEach(weapon => problems.AddRange(ContentRules.Weapon(weapon)));
                WeaponCatalog catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(DesignContent.WeaponCatalogPath);
                problems.AddRange(ContentRules.Catalog(catalog.name, Entries(catalog.Count, catalog.Get), weapons));
                problems.AddRange(ContentRules.Unique("weapon names", weapons.Select(weapon => Serialized(weapon, "displayName"))));
                break;
            }
            case "Abilities":
            {
                List<AbilityDefinition> abilities = Find<AbilityDefinition>(DataFolders, problems, kind);
                abilities.ForEach(ability => problems.AddRange(ContentRules.Ability(ability)));
                problems.AddRange(ContentRules.Unique("ability names", abilities.Select(ability => Serialized(ability, "displayName"))));
                break;
            }
            case "Classes":
                Find<PlayerClass>(DataFolders, problems, kind).ForEach(playerClass => problems.AddRange(ContentRules.Class(playerClass)));
                break;
            case "Statuses":
            {
                List<StatusEffectDefinition> statuses = Find<StatusEffectDefinition>(DataFolders, problems, kind);
                statuses.ForEach(status => problems.AddRange(ContentRules.Status(status)));
                StatusEffectCatalog catalog = AssetDatabase.LoadAssetAtPath<CombatSettings>(DesignContent.CombatSettingsPath).StatusCatalog;
                problems.AddRange(ContentRules.Catalog(catalog.name, Entries(catalog.Count, catalog.Get), statuses));
                problems.AddRange(ContentRules.Unique("status names", statuses.Select(status => Serialized(status, "displayName"))));
                problems.AddRange(ContentRules.Unique("status abbreviations", statuses.Select(status => status.Abbreviation)));
                break;
            }
            case "Combat profiles":
                Find<CombatProfile>(DataFolders, problems, kind).ForEach(profile => problems.AddRange(ContentRules.Profile(profile)));
                break;
            case "Mob configs":
                Find<MobConfig>(MobFolders, problems, kind).ForEach(mob => problems.AddRange(ContentRules.Mob(mob)));
                break;
            case "Looks":
                Find<CharacterLookCatalog>(DataFolders, problems, kind).ForEach(looks => problems.AddRange(ContentRules.Looks(looks)));
                break;
            case "Character colours":
                Find<CharacterColours>(DataFolders, problems, kind).ForEach(colours => problems.AddRange(ContentRules.Colours(colours)));
                break;
            case "Combat settings":
                Find<CombatSettings>(DataFolders, problems, kind).ForEach(settings => problems.AddRange(ContentRules.Combat(settings)));
                break;
            default:
                problems.Add($"No check for {kind}.");
                break;
        }

        return problems;
    }

    /// <summary>
    /// Every asset of type <typeparamref name="T"/> under <paramref name="folders"/>; a problem when there is none.
    /// </summary>
    private static List<T> Find<T>(string[] folders, List<string> problems, string kind) where T : Object
    {
        List<T> assets = AssetDatabase.FindAssets($"t:{typeof(T).Name}", folders)
            .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset != null)
            .ToList();
        if (assets.Count == 0)
        {
            problems.Add($"No {kind} found under {string.Join(", ", folders)}.");
        }

        return assets;
    }

    /// <summary>
    /// A catalog's entries in order.
    /// </summary>
    private static List<Object> Entries<T>(int count, System.Func<int, T> get) where T : Object
    {
        List<Object> entries = new();
        for (int i = 0; i < count; i++)
        {
            entries.Add(get(i));
        }

        return entries;
    }

    /// <summary>
    /// A definition's serialized string at <paramref name="path"/>.
    /// </summary>
    private static string Serialized(Object definition, string path)
    {
        return new SerializedObject(definition).FindProperty(path).stringValue;
    }

    /// <summary>
    /// Asserts that each of <paramref name="expected"/> appears in one of <paramref name="problems"/>.
    /// </summary>
    private static void AssertReports(List<string> problems, params string[] expected)
    {
        foreach (string part in expected)
        {
            Assert.That(problems.Any(problem => problem.Contains(part)), Is.True, $"Expected a problem naming {part}; got:\n{string.Join("\n", problems)}");
        }
    }

    /// <summary>
    /// Gives this test's own <paramref name="status"/> a description.
    /// </summary>
    private static void Describe(StatusEffectDefinition status, string description)
    {
        SerializedObject data = new(status);
        data.FindProperty("description").stringValue = description;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// A sprite for a test weapon's icon, its texture tracked.
    /// </summary>
    private Sprite Icon()
    {
        Texture2D texture = Track(new Texture2D(1, 1));
        return Track(Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.zero));
    }

    /// <summary>
    /// Keeps <paramref name="instance"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
