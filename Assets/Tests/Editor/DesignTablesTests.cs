using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the design tables (Docs/DesignSystem.md, Phase 1): the committed files in <c>Docs/Generated</c> match
/// what <see cref="DesignTables"/> builds from the assets, and the derived columns follow the game's rules.
/// </summary>
public class DesignTablesTests
{
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
    /// Every committed table equals what the tool builds from the project's assets, and the folder holds no other
    /// table.
    /// </summary>
    [Test]
    public void TheCommittedTables_MatchTheAssets()
    {
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, DesignTables.Folder);
        HashSet<string> built = new();
        foreach (DesignTable table in DesignTables.Build())
        {
            built.Add(table.FileName);
            string path = Path.Combine(folder, table.FileName);
            string message = $"{DesignTables.Folder}/{table.FileName} is out of date. Run {DesignTables.MenuPath} and commit the result; after a merge, run it again rather than merging the tables by hand.";
            Assert.That(File.Exists(path), Is.True, message);
            Assert.That(Normalise(File.ReadAllText(path)), Is.EqualTo(Normalise(table.Markdown)), message);
        }

        foreach (string path in Directory.GetFiles(folder))
        {
            Assert.That(built.Contains(Path.GetFileName(path)), Is.True, $"{path} is not a table {DesignTables.MenuPath} writes; remove it.");
        }
    }

    /// <summary>
    /// The attack period is the cooldown, or the time until the attack may be cut short when that is longer: the end
    /// of the contact with a recovery orders may end, the end of the timeline without.
    /// </summary>
    [Test]
    public void AttackPeriod_IsTheCooldown_OrTheTimeUntilTheAttackMayBeCutShort()
    {
        AttackTimeline timeline = new(0.3f, 0.1f, 0.4f);

        Assert.That(DesignMath.AttackPeriod(1f, timeline, true), Is.EqualTo(1f));
        Assert.That(DesignMath.AttackPeriod(0.1f, timeline, true), Is.EqualTo(0.4f).Within(1e-5f));
        Assert.That(DesignMath.AttackPeriod(0.1f, timeline, false), Is.EqualTo(0.8f).Within(1e-5f));
    }

    /// <summary>
    /// Hits to kill round up and are 0 for a hit that deals nothing; the time to kill counts every period but the
    /// last, then the windup to the killing contact.
    /// </summary>
    [Test]
    public void HitsAndTimeToKill_RoundUp_AndEndAtTheKillingContact()
    {
        Assert.That(DesignMath.HitsToKill(100, 20), Is.EqualTo(5));
        Assert.That(DesignMath.HitsToKill(100, 30), Is.EqualTo(4));
        Assert.That(DesignMath.HitsToKill(100, 0), Is.EqualTo(0));
        Assert.That(DesignMath.TimeToKill(5, 0.35f, 0.15f), Is.EqualTo(1.55f).Within(1e-5f));
        Assert.That(DesignMath.TimeToKill(1, 0.35f, 0.15f), Is.EqualTo(0.15f).Within(1e-5f));
    }

    /// <summary>
    /// The periodic tick count is the number of ticks <see cref="StatusEffectSet"/> deals over a status's whole
    /// duration, the tick on the instant it expires included.
    /// </summary>
    [TestCase(4f, 1f)]
    [TestCase(4.5f, 1f)]
    [TestCase(3f, 0.5f)]
    [TestCase(2.5f, 0.75f)]
    public void PeriodicTicks_MatchTheTicksAStatusDeals(float duration, float interval)
    {
        StatusEffectDefinition burn = Track(StatusEffectDefinition.Create(
            "Burn", StatusKind.Debuff, duration, periodic: StatusPeriodic.Damage, periodicAmount: 3, tickInterval: interval));
        StatusEffectSet set = new();
        List<StatusTick> ticks = new();

        set.Apply(burn, null, 1f);
        set.Tick(duration + 10f, ticks);

        Assert.That(DesignMath.PeriodicTicks(duration, interval), Is.EqualTo(ticks.Count));
    }

    /// <summary>
    /// The contrast ratio of known pairs: white on black is 21, a colour on itself 1, and the Physical popup red on
    /// black about 3.1.
    /// </summary>
    [Test]
    public void ContrastRatio_MatchesKnownPairs()
    {
        Assert.That(DesignMath.ContrastRatio(Color.white, Color.black), Is.EqualTo(21.0).Within(1e-6));
        Assert.That(DesignMath.ContrastRatio(Color.grey, Color.grey), Is.EqualTo(1.0).Within(1e-6));
        Assert.That(DesignMath.ContrastRatio(new Color(0.7f, 0.12f, 0.12f), Color.black), Is.EqualTo(3.11).Within(0.01));
    }

    /// <summary>
    /// The tables built from test content show each hit resolved against its target's profile, with the hits and the
    /// time to kill, and a stacking status's periodic totals.
    /// </summary>
    [Test]
    public void Build_ResolvesEachAttackAgainstItsTarget()
    {
        StatusEffectDefinition burn = Track(StatusEffectDefinition.Create(
            "Test Burn", StatusKind.Debuff, 4f, StatusStacking.AddStack, 5,
            periodic: StatusPeriodic.Damage, periodicAmount: 3, periodicDamageType: DamageType.Fire, tickInterval: 1f));
        CombatSettings combat = Track(CombatSettings.Create(Track(StatusEffectCatalog.Create(burn))));
        PlayerWeapon sword = Track(PlayerWeapon.Create("Test Sword", 20, 0.5f, 0.65f, null, windup: 0.1f, contact: 0.05f, recovery: 0.2f));
        CombatProfile mobProfile = Track(CombatProfile.Create(Faction.Mobs, resistances: new DamageResistance(DamageType.Physical, 50)));
        CombatProfile playerProfile = Track(CombatProfile.Create(Faction.Players));
        MobConfig mobConfig = Track(MobConfig.Create(
            attackDamage: 30, attackInterval: 1f, attackWindup: 0.5f, attackContact: 0.1f, attackRecovery: 0.2f));
        DesignContent content = new(
            combat,
            Track(WeaponCatalog.Create(sword)),
            new AbilityDefinition[0],
            new PlayerClass[0],
            new[]
            {
                new DesignUnit("Test Mob", 100, mobProfile, 2f, mobConfig, null),
                new DesignUnit("Test Player", 90, playerProfile, 5f, null, sword),
            });

        Dictionary<string, string> tables = new();
        foreach (DesignTable table in DesignTables.Build(content))
        {
            tables.Add(table.FileName, table.Markdown);
        }

        Assert.That(tables["Weapons.md"], Does.Contain("| Test Sword | Test Mob | 10 Physical (resisted) | 20 | 10 | 4.6 s |"));
        Assert.That(tables["Units.md"], Does.Contain("| Test Mob | Test Player | 30 Physical | 30 | 3 | 2.5 s |"));
        Assert.That(tables["StatusEffects.md"], Does.Contain("| 3 Fire every 1 s | 12 (60 at 5) |"));
    }

    /// <summary>
    /// The theme's table lists each role and each size with its body height at 1080p, and each pair of roles a themed
    /// text uses with its contrast and threshold; the popup colours are measured against the theme's Outline.
    /// </summary>
    [Test]
    public void Build_WritesTheThemeTable_AndMeasuresPopupsOnTheOutline()
    {
        Dictionary<UiColour, Color> roles = TestThemes.Roles();
        roles[UiColour.Outline] = Color.white;
        UiTheme theme = Track(UiTheme.Create(roles, TestThemes.Shared.TextSizes));
        ThemedText text = Track(new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI))).AddComponent<ThemedText>();
        text.Configure(theme, UiColour.Text, UiColour.SurfaceRaised, UiTextSize.Body);
        CombatSettings combat = Track(CombatSettings.Create(Track(StatusEffectCatalog.Create())));
        DesignContent content = new(
            combat,
            Track(WeaponCatalog.Create()),
            new AbilityDefinition[0],
            new PlayerClass[0],
            new DesignUnit[0],
            theme,
            new[] { new UiThemeUsage.TextUse(text, "test") });

        Dictionary<string, string> tables = new();
        foreach (DesignTable table in DesignTables.Build(content))
        {
            tables.Add(table.FileName, table.Markdown);
        }

        string contrast = (System.Math.Floor(DesignMath.ContrastRatio(roles[UiColour.Text], roles[UiColour.SurfaceRaised]) * 100.0) / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        string physical = (System.Math.Floor(DesignMath.ContrastRatio(combat.GetColor(DamageType.Physical), Color.white) * 100.0) / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(tables["UiTheme.md"], Does.Contain("| Outline | `#FFFFFF` | 100% |"));
        Assert.That(tables["UiTheme.md"], Does.Contain("| Shade | `#000000` | 65% |"));
        Assert.That(tables["UiTheme.md"], Does.Contain("| Body | Galmuri7 SDF | 8 | 24 px |"));
        Assert.That(tables["UiTheme.md"], Does.Contain($"| Text | SurfaceRaised | Body | {contrast}:1 | 4.50:1 |"));
        Assert.That(tables["DamageTypes.md"], Does.Contain($"| Physical | `#{ColorUtility.ToHtmlStringRGB(combat.GetColor(DamageType.Physical))}` | {physical}:1 |"));
    }

    /// <summary>
    /// Keeps <paramref name="instance"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// <paramref name="text"/> with LF line endings.
    /// </summary>
    private static string Normalise(string text)
    {
        return text.Replace("\r\n", "\n");
    }
}
