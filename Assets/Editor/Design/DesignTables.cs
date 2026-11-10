using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The design tables (Docs/DesignSystem.md, Generated tables): one Markdown file per kind of content in
/// <see cref="Folder"/>, built from the assets and the game's own formulas (<see cref="DesignMath"/>,
/// <see cref="DamageMath"/>) in the invariant culture, rows in catalog order or by name. <see cref="Build()"/> returns
/// them, <see cref="Write"/> writes them, and <c>DesignTablesTests</c> fails while the committed files differ.
/// </summary>
public static class DesignTables
{
    public const string Folder = "Docs/Generated";
    public const string MenuPath = "Tools/TopDownRPG/Design/Write Design Tables";

    private static readonly string[] ClassSlotKeys = { "Q", "W", "E", "R" };
    private static readonly string[] WeaponSlotKeys = { "1", "2" };
    private const int BodyZoom = 3;

    private const string Note =
        "Written by `" + MenuPath + "` from the project's assets. Do not edit it by hand: change the assets and run the "
        + "menu item again. The derived columns are defined in [DesignSystem.md](../DesignSystem.md#generated-tables), at "
        + "attack speed 1 and against a unit's base defence, without statuses.";

    /// <summary>
    /// The tables built from the project's assets.
    /// </summary>
    public static IReadOnlyList<DesignTable> Build()
    {
        return Build(DesignContent.FromProject());
    }

    /// <summary>
    /// The tables built from <paramref name="content"/>: damage types, weapons, abilities, statuses, units and the UI
    /// theme.
    /// </summary>
    public static IReadOnlyList<DesignTable> Build(DesignContent content)
    {
        return new[]
        {
            DamageTypes(content),
            Weapons(content),
            Abilities(content),
            StatusEffects(content),
            Units(content),
            Theme(content),
        };
    }

    /// <summary>
    /// Writes every table of the project into <see cref="Folder"/>, with LF line endings and no byte order mark.
    /// </summary>
    public static void Write()
    {
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder);
        Directory.CreateDirectory(folder);
        foreach (DesignTable table in Build())
        {
            File.WriteAllText(Path.Combine(folder, table.FileName), table.Markdown, new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Writes the project's tables from the menu.
    /// </summary>
    [MenuItem(MenuPath)]
    private static void WriteFromMenu()
    {
        Write();
        Debug.Log($"Wrote the design tables into {Folder}.");
    }

    /// <summary>
    /// Each damage type's popup colour and its contrast on the theme's Outline (black without a theme), the heal and
    /// immune popups, and the resolution rules.
    /// </summary>
    private static DesignTable DamageTypes(DesignContent content)
    {
        CombatSettings combat = content.Combat;
        Color outline = content.Theme != null ? content.Theme.Colour(UiColour.Outline) : Color.black;
        StringBuilder text = Begin("Damage types");
        Row(text, "Type", "Popup colour", "Contrast on Outline");
        Rule(text, 3);
        foreach (DamageType type in (DamageType[])Enum.GetValues(typeof(DamageType)))
        {
            Row(text, type.ToString(), Hex(combat.GetColor(type)), Contrast(combat.GetColor(type), outline));
        }

        Row(text, "Heal", Hex(combat.HealColor), Contrast(combat.HealColor, outline));
        Row(text, $"Immune (\"{combat.ImmuneText}\")", Hex(combat.ImmuneColor), Contrast(combat.ImmuneColor, outline));

        text.Append("\n## Resolution\n\n");
        Row(text, "Rule", "Value");
        Rule(text, 2);
        Row(text, "Resistance floor", $"{combat.ResistanceFloor}%");
        Row(text, "Resistance cap", $"{combat.ResistanceCap}%");
        Row(text, "Minimum damage", Number(combat.MinimumDamage));
        Row(text, "Periodic popup size", combat.PeriodicPopupSize.ToString());
        return new DesignTable("DamageTypes.md", text.ToString());
    }

    /// <summary>
    /// The UI theme: each colour role, each text size with its font and body height at 1080p (zoom 3), and the contrast
    /// of every pair of text and background roles a themed text uses against its threshold.
    /// </summary>
    private static DesignTable Theme(DesignContent content)
    {
        UiTheme theme = content.Theme;
        StringBuilder text = Begin("UI theme");
        if (theme == null)
        {
            return new DesignTable("UiTheme.md", text.ToString());
        }

        text.Append("## Colour roles\n\n");
        Row(text, "Role", "Colour", "Opacity");
        Rule(text, 3);
        foreach (UiColour role in (UiColour[])Enum.GetValues(typeof(UiColour)))
        {
            Color colour = theme.Colour(role);
            Row(text, role.ToString(), Hex(colour), Percent(colour.a));
        }

        text.Append("\n## Text sizes\n\n");
        Row(text, "Size", "Font", "UI px", "Body at 1080p");
        Rule(text, 4);
        foreach (UiTextSize size in (UiTextSize[])Enum.GetValues(typeof(UiTextSize)))
        {
            UiTheme.TextStyle style = theme.Style(size);
            string font = style.Font != null ? style.Font.name : "none";
            Row(text, size.ToString(), font, Number(style.Pixels), $"{Number(UiThemeUsage.BodyHeight(style) * BodyZoom)} px");
        }

        text.Append("\n## Text on backgrounds\n\nEach pair of text role and background role a themed text uses, at the smallest size it is used at. A translucent background counts over white and over black, the lower ratio; a control in each state it is read in.\n\n");
        Row(text, "Text", "Background", "Smallest size", "Contrast", "Needs");
        Rule(text, 5);
        foreach (UiThemeUsage.PairContrast pair in UiThemeUsage.Measure(theme, content.TextUses))
        {
            Row(text, pair.Text.ToString(), pair.Background, pair.Size.ToString(), Ratio(pair.Ratio), Ratio(pair.Threshold));
        }

        return new DesignTable("UiTheme.md", text.ToString());
    }

    /// <summary>
    /// Each weapon of the catalog with its attack, then what each attack does to each mob.
    /// </summary>
    private static DesignTable Weapons(DesignContent content)
    {
        WeaponCatalog catalog = content.Weapons;
        StringBuilder text = Begin("Weapons");
        Row(text, "Index", "Weapon", "Hit", "Cooldown", "Windup, contact, recovery", "Cancel rules", "Attack range", "Delivery", "Abilities", "Attack period", "Damage per second");
        Rule(text, 11);
        for (int i = 0; i < catalog.Count; i++)
        {
            PlayerWeapon weapon = catalog.Get(i);
            if (weapon == null)
            {
                Row(text, Number(i), "(empty)", "", "", "", "", "", "", "", "", "");
                continue;
            }

            float period = WeaponPeriod(weapon);
            Row(
                text,
                Number(i),
                weapon.DisplayName,
                HitText(weapon.Hit),
                Seconds(weapon.AttackCooldown),
                TimelineText(weapon.Timeline),
                CancelRules(weapon),
                Number(weapon.AttackRange),
                Delivery(weapon),
                WeaponAbilities(weapon),
                Seconds(period),
                Rate(weapon.Damage, period));
        }

        text.Append("\n## Against each mob\n\nA projectile's flight time is not counted.\n\n");
        Row(text, "Weapon", "Mob", "Hit dealt", "Damage per second", "Hits to kill", "Time to kill");
        Rule(text, 6);
        for (int i = 0; i < catalog.Count; i++)
        {
            PlayerWeapon weapon = catalog.Get(i);
            if (weapon == null)
            {
                continue;
            }

            foreach (DesignUnit mob in content.Units)
            {
                if (mob.IsMob)
                {
                    DamageResult dealt = DesignMath.Resolve(weapon.Damage, weapon.DamageType, mob.Profile, content.Combat);
                    KillRow(text, weapon.DisplayName, mob, dealt, WeaponPeriod(weapon), weapon.Timeline.Windup);
                }
            }
        }

        return new DesignTable("Weapons.md", text.ToString());
    }

    /// <summary>
    /// Every ability by name: who uses it, how it is cast, what it does and how it looks.
    /// </summary>
    private static DesignTable Abilities(DesignContent content)
    {
        StringBuilder text = Begin("Abilities");
        Row(text, "Ability", "Used by", "Targeting", "Range", "Area", "Cast time", "Cooldown", "Movement", "Bufferable", "Effect", "Damage per cooldown second", "Cast clips");
        Rule(text, 12);
        foreach (AbilityDefinition ability in content.Abilities)
        {
            Row(
                text,
                ability.DisplayName,
                UsedBy(ability, content),
                Targeting(ability),
                ability.Targeting == AbilityTargeting.None ? "-" : Number(ability.Range),
                Area(ability),
                ability.CastTime > 0f ? Seconds(ability.CastTime) : "instant",
                Seconds(ability.Cooldown),
                ability.CastMovement.ToString(),
                ability.Bufferable ? "yes" : "no",
                Effect(ability),
                ability.Hit.Amount > 0 && ability.Cooldown > 0f ? Rate(ability.Hit.Amount, ability.Cooldown) : "-",
                CastClips(ability));
        }

        return new DesignTable("Abilities.md", text.ToString());
    }

    /// <summary>
    /// Each status of the catalog: its rules and periodic totals, then how it shows.
    /// </summary>
    private static DesignTable StatusEffects(DesignContent content)
    {
        StatusEffectCatalog catalog = content.Statuses;
        int count = catalog != null ? catalog.Count : 0;
        StringBuilder text = Begin("Status effects");
        text.Append("Modifiers are per stack. A periodic total is before the target's defence, at one stack and at the cap.\n\n");
        Row(text, "Index", "Status", "Kind", "Tags", "Duration", "Stacking", "Periodic", "Periodic total", "Modifiers", "Controls", "Grants");
        Rule(text, 11);
        for (int i = 0; i < count; i++)
        {
            StatusEffectDefinition status = catalog.Get(i);
            if (status == null)
            {
                Row(text, Number(i), "(empty)", "", "", "", "", "", "", "", "", "");
                continue;
            }

            Row(
                text,
                Number(i),
                status.DisplayName,
                status.Kind.ToString(),
                Flags(status.Tags),
                Seconds(status.Duration),
                Stacking(status),
                Periodic(status),
                PeriodicTotal(status),
                Modifiers(status),
                Flags(status.Controls),
                Grants(status));
        }

        text.Append("\n## Display\n\n");
        Row(text, "Index", "Status", "Abbreviation", "Colour", "Description");
        Rule(text, 5);
        for (int i = 0; i < count; i++)
        {
            StatusEffectDefinition status = catalog.Get(i);
            if (status != null)
            {
                Row(text, Number(i), status.DisplayName, status.Abbreviation, Hex(status.Color), string.IsNullOrWhiteSpace(status.Description) ? "-" : status.Description);
            }
        }

        return new DesignTable("StatusEffects.md", text.ToString());
    }

    /// <summary>
    /// The players and the mobs, then what each mob's attack does to each player.
    /// </summary>
    private static DesignTable Units(DesignContent content)
    {
        StringBuilder text = Begin("Units");
        Row(text, "Unit", "Faction", "Health", "Resistances", "Immunities", "Move speed", "Attack");
        Rule(text, 7);
        foreach (DesignUnit unit in content.Units)
        {
            Row(
                text,
                unit.Name,
                unit.Faction.ToString(),
                Number(unit.Health),
                Resistances(unit.Profile),
                Immunities(unit.Profile),
                $"{Number(unit.MoveSpeed)} units/s",
                unit.IsMob ? MobAttack(unit.Mob) : PlayerAttack(unit));
        }

        text.Append("\n## Each mob against each player\n\n");
        Row(text, "Mob", "Player", "Hit dealt", "Damage per second", "Hits to kill", "Time to kill");
        Rule(text, 6);
        foreach (DesignUnit mob in content.Units)
        {
            if (!mob.IsMob)
            {
                continue;
            }

            foreach (DesignUnit player in content.Units)
            {
                if (!player.IsMob)
                {
                    DamageResult dealt = DesignMath.Resolve(mob.Mob.AttackDamage, mob.Mob.AttackDamageType, player.Profile, content.Combat);
                    KillRow(text, mob.Name, player, dealt, MobPeriod(mob.Mob), mob.Mob.AttackTimeline.Windup);
                }
            }
        }

        return new DesignTable("Units.md", text.ToString());
    }

    /// <summary>
    /// A row of an attacker against a target: the hit dealt, the damage per second and the hits and time to kill.
    /// </summary>
    private static void KillRow(StringBuilder text, string attacker, DesignUnit target, DamageResult dealt, float period, float windup)
    {
        int hits = DesignMath.HitsToKill(target.Health, dealt.Amount);
        Row(
            text,
            attacker,
            target.Name,
            DealtText(dealt),
            Rate(dealt.Amount, period),
            hits > 0 ? Number(hits) : "never",
            hits > 0 ? Seconds(DesignMath.TimeToKill(hits, period, windup)) : "never");
    }

    /// <summary>
    /// A weapon's attack period at attack speed 1.
    /// </summary>
    private static float WeaponPeriod(PlayerWeapon weapon)
    {
        return DesignMath.AttackPeriod(weapon.AttackCooldown, weapon.Timeline, weapon.RecoveryCancellable);
    }

    /// <summary>
    /// A mob's attack period: its attack's recovery always plays out.
    /// </summary>
    private static float MobPeriod(MobConfig mob)
    {
        return DesignMath.AttackPeriod(mob.AttackInterval, mob.AttackTimeline, false);
    }

    /// <summary>
    /// A hit's amount and type, and the statuses it applies.
    /// </summary>
    private static string HitText(Hit hit)
    {
        StringBuilder text = new($"{hit.Amount} {hit.Type}");
        for (int i = 0; i < hit.StatusCount; i++)
        {
            StatusEffectDefinition status = hit.GetStatus(i);
            if (status != null)
            {
                text.Append(", ").Append(status.DisplayName);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// A resolved hit: its amount and type, marked resisted or a weakness, or immune.
    /// </summary>
    private static string DealtText(DamageResult dealt)
    {
        if (dealt.IsImmune)
        {
            return "immune";
        }

        string text = $"{dealt.Amount} {dealt.Type}";
        if ((dealt.Flags & DamageFlags.Resisted) != 0)
        {
            return text + " (resisted)";
        }

        return (dealt.Flags & DamageFlags.Weakness) != 0 ? text + " (weakness)" : text;
    }

    /// <summary>
    /// An attack timeline's windup, contact and recovery.
    /// </summary>
    private static string TimelineText(in AttackTimeline timeline)
    {
        return $"{Number(timeline.Windup)}, {Number(timeline.Contact)}, {Number(timeline.Recovery)} s";
    }

    /// <summary>
    /// A weapon's cancel rules.
    /// </summary>
    private static string CancelRules(PlayerWeapon weapon)
    {
        string windup = weapon.WindupCancelledByMove ? "a move cancels the windup" : "the windup holds";
        string recovery = weapon.RecoveryCancellable ? "any order ends the recovery" : "the recovery plays out";
        return $"{windup}; {recovery}";
    }

    /// <summary>
    /// How a weapon's hit lands: its melee area, or its projectile.
    /// </summary>
    private static string Delivery(PlayerWeapon weapon)
    {
        if (!weapon.FiresProjectile)
        {
            return $"melee, {Number(weapon.HitSize.x)} x {Number(weapon.HitSize.y)} area {Number(weapon.HitDistance)} ahead";
        }

        ProjectileSpec projectile = weapon.Projectile;
        string homing = projectile.Homing ? ", homing" : string.Empty;
        return $"projectile, {Number(projectile.Speed)} units/s, range {Number(projectile.Range)}, radius {Number(projectile.Radius)}{homing}";
    }

    /// <summary>
    /// A weapon's two abilities, each with its slot's default key.
    /// </summary>
    private static string WeaponAbilities(PlayerWeapon weapon)
    {
        List<string> names = new();
        for (int i = 0; i < PlayerWeapon.AbilityCount; i++)
        {
            AbilityDefinition ability = weapon.GetAbility(i);
            if (ability != null)
            {
                names.Add($"{ability.DisplayName} ({WeaponSlotKeys[i]})");
            }
        }

        return names.Count > 0 ? string.Join(", ", names) : "none";
    }

    /// <summary>
    /// The classes and weapons whose slots hold <paramref name="ability"/>, each with the slot's default key.
    /// </summary>
    private static string UsedBy(AbilityDefinition ability, DesignContent content)
    {
        List<string> users = new();
        foreach (PlayerClass playerClass in content.Classes)
        {
            for (int i = 0; i < PlayerClass.AbilityCount; i++)
            {
                if (playerClass.GetAbility(i) == ability)
                {
                    users.Add($"{playerClass.name} ({ClassSlotKeys[i]})");
                }
            }
        }

        for (int w = 0; w < content.Weapons.Count; w++)
        {
            PlayerWeapon weapon = content.Weapons.Get(w);
            for (int i = 0; weapon != null && i < PlayerWeapon.AbilityCount; i++)
            {
                if (weapon.GetAbility(i) == ability)
                {
                    users.Add($"{weapon.DisplayName} ({WeaponSlotKeys[i]})");
                }
            }
        }

        return users.Count > 0 ? string.Join(", ", users) : "unused";
    }

    /// <summary>
    /// An ability's targeting, with the units a unit-targeted one accepts.
    /// </summary>
    private static string Targeting(AbilityDefinition ability)
    {
        if (ability.Targeting != AbilityTargeting.Unit)
        {
            return ability.Targeting.ToString();
        }

        return ability.UnitFilter switch
        {
            AbilityUnitFilter.Enemy => "Unit (enemies)",
            AbilityUnitFilter.Ally => "Unit (allies)",
            _ => "Unit (any)",
        };
    }

    /// <summary>
    /// Where an ability's effect searches for units.
    /// </summary>
    private static string Area(AbilityDefinition ability)
    {
        float radius = ability.AreaRadius;
        return ability.Targeting switch
        {
            AbilityTargeting.None => radius > 0f ? $"{Number(radius)} around the caster" : "the caster",
            AbilityTargeting.Point => $"{Number(radius)} around the point",
            AbilityTargeting.Direction => $"a line {Number(2f * radius)} wide",
            _ => "the target",
        };
    }

    /// <summary>
    /// What an ability does to each unit it affects: its hit and statuses, and its heal.
    /// </summary>
    private static string Effect(AbilityDefinition ability)
    {
        List<string> parts = new();
        Hit hit = ability.Hit;
        if (hit.Amount > 0)
        {
            parts.Add($"{hit.Amount} {hit.Type}");
        }

        if (ability.Heal > 0)
        {
            parts.Add($"heals {ability.Heal}");
        }

        for (int i = 0; i < hit.StatusCount; i++)
        {
            StatusEffectDefinition status = hit.GetStatus(i);
            if (status != null)
            {
                parts.Add(status.DisplayName);
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "none";
    }

    /// <summary>
    /// The clips an ability's cast plays, standing and over the run.
    /// </summary>
    private static string CastClips(AbilityDefinition ability)
    {
        List<string> clips = new();
        if (!string.IsNullOrEmpty(ability.CastClip))
        {
            clips.Add($"`{ability.CastClip}`");
        }

        if (!string.IsNullOrEmpty(ability.MovingCastClip))
        {
            clips.Add($"`{ability.MovingCastClip}`");
        }

        return clips.Count > 0 ? string.Join(", ", clips) : "none";
    }

    /// <summary>
    /// How a status stacks.
    /// </summary>
    private static string Stacking(StatusEffectDefinition status)
    {
        return status.Stacking switch
        {
            StatusStacking.AddStack => $"stacks to {status.MaxStacks}",
            StatusStacking.Independent => $"independent, up to {status.MaxStacks}",
            _ => "refresh",
        };
    }

    /// <summary>
    /// What a status does each tick, or "none".
    /// </summary>
    private static string Periodic(StatusEffectDefinition status)
    {
        if (!status.HasPeriodicEffect)
        {
            return "none";
        }

        string effect = status.Periodic == StatusPeriodic.Heal
            ? $"heals {status.PeriodicAmount}"
            : $"{status.PeriodicAmount} {status.PeriodicDamageType}";
        return $"{effect} every {Seconds(status.TickInterval)}";
    }

    /// <summary>
    /// What a periodic status deals or heals over its whole duration at one stack, and at its cap when it has one.
    /// </summary>
    private static string PeriodicTotal(StatusEffectDefinition status)
    {
        if (!status.HasPeriodicEffect)
        {
            return "-";
        }

        int single = DesignMath.PeriodicTicks(status.Duration, status.TickInterval) * status.PeriodicAmount;
        bool stacks = status.Stacking != StatusStacking.Refresh && status.MaxStacks > 1;
        return stacks ? $"{single} ({single * status.MaxStacks} at {status.MaxStacks})" : Number(single);
    }

    /// <summary>
    /// A status's modifiers per stack: move speed, damage dealt and taken, and resistances.
    /// </summary>
    private static string Modifiers(StatusEffectDefinition status)
    {
        List<string> parts = new();
        if (!Mathf.Approximately(status.MoveSpeedMultiplier, 1f))
        {
            parts.Add($"move x{Number(status.MoveSpeedMultiplier)}");
        }

        if (!Mathf.Approximately(status.DamageDealtMultiplier, 1f))
        {
            parts.Add($"damage dealt x{Number(status.DamageDealtMultiplier)}");
        }

        if (!Mathf.Approximately(status.DamageTakenMultiplier, 1f))
        {
            parts.Add($"damage taken x{Number(status.DamageTakenMultiplier)}");
        }

        foreach (DamageType type in (DamageType[])Enum.GetValues(typeof(DamageType)))
        {
            int delta = status.GetResistanceDelta(type);
            if (delta != 0)
            {
                parts.Add($"{(delta > 0 ? "+" : string.Empty)}{delta}% {type} resistance");
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "none";
    }

    /// <summary>
    /// The immunities a status grants while it is active.
    /// </summary>
    private static string Grants(StatusEffectDefinition status)
    {
        List<string> parts = new();
        if (status.GrantsDamageImmunity != DamageTypeMask.None)
        {
            parts.Add($"immune to {status.GrantsDamageImmunity} damage");
        }

        if (status.GrantsStatusImmunity != StatusTags.None)
        {
            parts.Add($"immune to {status.GrantsStatusImmunity} statuses");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "none";
    }

    /// <summary>
    /// A profile's resistances per damage type, or "none".
    /// </summary>
    private static string Resistances(CombatProfile profile)
    {
        List<string> parts = new();
        foreach (DamageType type in (DamageType[])Enum.GetValues(typeof(DamageType)))
        {
            int percent = profile != null ? profile.GetResistance(type) : 0;
            if (percent != 0)
            {
                parts.Add($"{type} {percent}%");
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "none";
    }

    /// <summary>
    /// A profile's damage and status immunities, or "none".
    /// </summary>
    private static string Immunities(CombatProfile profile)
    {
        List<string> parts = new();
        if (profile != null && profile.DamageImmunities != DamageTypeMask.None)
        {
            parts.Add($"{profile.DamageImmunities} damage");
        }

        if (profile != null && profile.StatusImmunities != StatusTags.None)
        {
            parts.Add($"{profile.StatusImmunities} statuses");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "none";
    }

    /// <summary>
    /// A mob's attack: its hit, cooldown, timeline, area and period.
    /// </summary>
    private static string MobAttack(MobConfig mob)
    {
        StringBuilder text = new($"{mob.AttackDamage} {mob.AttackDamageType}");
        foreach (StatusEffectDefinition status in mob.AttackStatuses)
        {
            if (status != null)
            {
                text.Append(", ").Append(status.DisplayName);
            }
        }

        text.Append($"; cooldown {Seconds(mob.AttackInterval)}; {TimelineText(mob.AttackTimeline)}");
        text.Append($"; area {Number(mob.AttackArea.x)} x {Number(mob.AttackArea.y)}; period {Seconds(MobPeriod(mob))}");
        return text.ToString();
    }

    /// <summary>
    /// A player's attack: the weapon it holds, and the one it starts with.
    /// </summary>
    private static string PlayerAttack(DesignUnit player)
    {
        string start = player.StartingWeapon != null ? $", starting with the {player.StartingWeapon.DisplayName}" : string.Empty;
        return $"the weapon it holds ([Weapons.md](Weapons.md)){start}";
    }

    /// <summary>
    /// A flags value as its names, or "none".
    /// </summary>
    private static string Flags<T>(T value) where T : Enum
    {
        return Convert.ToInt64(value) == 0 ? "none" : value.ToString();
    }

    /// <summary>
    /// A new table file: its title and the note on where it comes from.
    /// </summary>
    private static StringBuilder Begin(string title)
    {
        StringBuilder text = new();
        text.Append("# ").Append(title).Append("\n\n").Append(Note).Append("\n\n");
        return text;
    }

    /// <summary>
    /// A Markdown table row of <paramref name="cells"/>, each escaped.
    /// </summary>
    private static void Row(StringBuilder text, params string[] cells)
    {
        text.Append('|');
        foreach (string cell in cells)
        {
            text.Append(' ').Append(Escape(cell)).Append(" |");
        }

        text.Append('\n');
    }

    /// <summary>
    /// The rule under a table's header of <paramref name="columns"/> columns.
    /// </summary>
    private static void Rule(StringBuilder text, int columns)
    {
        text.Append('|');
        for (int i = 0; i < columns; i++)
        {
            text.Append("---|");
        }

        text.Append('\n');
    }

    /// <summary>
    /// A cell's text on one line, its pipes escaped.
    /// </summary>
    private static string Escape(string cell)
    {
        return string.IsNullOrEmpty(cell)
            ? string.Empty
            : cell.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace("|", "\\|").Trim();
    }

    /// <summary>
    /// A number in the invariant culture with at most three decimals.
    /// </summary>
    private static string Number(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A duration in seconds.
    /// </summary>
    private static string Seconds(float value)
    {
        return $"{Number(value)} s";
    }

    /// <summary>
    /// An amount per <paramref name="seconds"/>, to one decimal; "-" when either is not positive.
    /// </summary>
    private static string Rate(int amount, float seconds)
    {
        return amount > 0 && seconds > 0f ? (amount / seconds).ToString("0.#", CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>
    /// A colour as an sRGB hex code.
    /// </summary>
    private static string Hex(Color colour)
    {
        return $"`#{ColorUtility.ToHtmlStringRGB(colour)}`";
    }

    /// <summary>
    /// The contrast ratio of two colours, cut (not rounded) to two decimals so a pair just under a threshold never
    /// reads as meeting it.
    /// </summary>
    private static string Contrast(Color a, Color b)
    {
        return Ratio(DesignMath.ContrastRatio(a, b));
    }

    /// <summary>
    /// A contrast ratio cut (not rounded) to two decimals.
    /// </summary>
    private static string Ratio(double ratio)
    {
        double cut = Math.Floor(ratio * 100.0) / 100.0;
        return $"{cut.ToString("0.00", CultureInfo.InvariantCulture)}:1";
    }

    /// <summary>
    /// An opacity as a whole percentage.
    /// </summary>
    private static string Percent(float value)
    {
        return $"{Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture)}%";
    }
}
