using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for players' looks (Docs/AsepriteCharacters.md, Phase 2): a catalog finds its looks by index and by name; a
/// player's look shows a catalog look on its visuals and ignores an index the catalog lacks; on the owner it follows
/// the local settings' choice, the first look for an empty or unknown name or no settings, raising each look it shows,
/// until it stops following; and the local settings keep the look's name across a restart, telling only of a change.
/// Its colours (Docs/PlayerCharacter.md, Phase 5) follow the local settings' picks the same way, the look's own for an
/// unknown swatch or the choice's own, shown on its palette, and a copy shows the picks it is given; the local settings
/// keep a swatch per colour choice across a restart, an empty one forgetting the pick, and read a file from before
/// colours with none. Its parts (Phase 6), on the project's adventurer: the composed outfit of the parts shown, and its
/// own set for its own parts; the owner follows the settings' part picks, the first option for an unknown name, leaving
/// the weapon's slot to the weapon; and the settings keep an option per slot across a restart. Its weapon's slot
/// (Phase 7) shows the gear of the weapon shown, none for a weapon without gear, whatever the picks.
/// </summary>
public class CharacterLookTests
{
    private readonly List<Object> created = new();
    private CharacterLocomotionSettings locomotion;
    private CharacterLocomotionSettings banditLocomotion;
    private CharacterAnimationSet rogueSet;
    private CharacterAnimationSet banditSet;
    private CharacterLookCatalog catalog;

    /// <summary>
    /// Builds a catalog of two looks, a rogue and a bandit, each with a set of its own.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        locomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        banditLocomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2f, 5f, 0.1f, 0.5f, 1.5f, 8f));
        rogueSet = Track(CharacterAnimationSet.Create(new CharacterAnimationClip[0]));
        banditSet = Track(CharacterAnimationSet.Create(new CharacterAnimationClip[0]));
        catalog = Track(CharacterLookCatalog.Create(new CharacterLook("Rogue", rogueSet, locomotion), new CharacterLook("Bandit", banditSet, banditLocomotion)));
    }

    /// <summary>
    /// Destroys everything the test made.
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
    /// A catalog gives its looks by index and the index of a look by name, nothing out of range or for an unknown or
    /// empty name.
    /// </summary>
    [Test]
    public void ACatalog_FindsItsLooksByIndexAndName()
    {
        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.Get(1).Name, Is.EqualTo("Bandit"));
        Assert.That(catalog.Get(1).AnimationSet, Is.EqualTo(banditSet));
        Assert.That(catalog.Get(1).Locomotion, Is.EqualTo(banditLocomotion));
        Assert.That(catalog.Get(-1), Is.Null);
        Assert.That(catalog.Get(2), Is.Null);
        Assert.That(catalog.IndexOf("Bandit"), Is.EqualTo(1));
        Assert.That(catalog.IndexOf("Rogue"), Is.EqualTo(0));
        Assert.That(catalog.IndexOf("Knight"), Is.EqualTo(-1));
        Assert.That(catalog.IndexOf(""), Is.EqualTo(-1));
        Assert.That(catalog.IndexOf(null), Is.EqualTo(-1));
    }

    /// <summary>
    /// Showing a look puts its set and locomotion settings on the visuals and remembers its index; an index the catalog
    /// lacks changes nothing.
    /// </summary>
    [Test]
    public void Show_PutsTheLookOnTheVisuals()
    {
        PlayerLook look = CreatePlayer(out CharacterVisuals visuals);

        Assert.That(look.Index, Is.EqualTo(-1));
        Assert.That(look.Show(1), Is.True);
        Assert.That(look.Index, Is.EqualTo(1));
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet));
        Assert.That(visuals.Locomotion, Is.EqualTo(banditLocomotion));
        Assert.That(look.Show(5), Is.False);
        Assert.That(look.Show(-1), Is.False);
        Assert.That(look.Index, Is.EqualTo(1));
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet));
    }

    /// <summary>
    /// The owner shows the look its settings name and raises it, then follows each change to it; an unknown name shows
    /// the first look. Once it stops following, a change shows nothing.
    /// </summary>
    [Test]
    public void FollowLocalChoice_ShowsTheSettingsLook_AndEveryChangeToIt()
    {
        UserSettings settings = new(new MemorySaveStore());
        settings.SetCharacterLook("Bandit");
        PlayerLook look = CreatePlayer(out CharacterVisuals visuals);
        look.Construct(settings, null);
        List<int> chosen = new();
        look.Chosen += chosen.Add;

        look.FollowLocalChoice();
        Assert.That(chosen, Is.EqualTo(new[] { 1 }));
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet));

        settings.SetCharacterLook("Rogue");
        Assert.That(chosen, Is.EqualTo(new[] { 1, 0 }));
        Assert.That(visuals.AnimationSet, Is.EqualTo(rogueSet));

        settings.SetCharacterLook("Knight");
        Assert.That(chosen, Is.EqualTo(new[] { 1, 0, 0 }), "An unknown look shows the first.");

        settings.SetCharacterLook("Bandit");
        look.StopFollowing();
        settings.SetCharacterLook("Rogue");
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet), "Stopped, it no longer follows.");
        Assert.That(chosen, Is.EqualTo(new[] { 1, 0, 0, 1 }));
    }

    /// <summary>
    /// Without settings (a player built without injection) the owner shows the first look.
    /// </summary>
    [Test]
    public void FollowLocalChoice_WithoutSettings_ShowsTheFirstLook()
    {
        PlayerLook look = CreatePlayer(out CharacterVisuals visuals);
        look.Show(1);

        look.FollowLocalChoice();

        Assert.That(look.Index, Is.EqualTo(0));
        Assert.That(visuals.AnimationSet, Is.EqualTo(rogueSet));
    }

    /// <summary>
    /// The settings keep the look's name in their file, which settings read again later find; setting the same name
    /// again neither writes nor tells of a change.
    /// </summary>
    [Test]
    public void TheSettings_KeepTheLooksName()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        int changes = 0;
        settings.CharacterLookChanged += () => changes++;
        Assert.That(settings.CharacterLook, Is.Empty);

        Assert.That(settings.SetCharacterLook("Bandit"), Is.True);
        Assert.That(settings.SetCharacterLook("Bandit"), Is.False);
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(new UserSettings(store).CharacterLook, Is.EqualTo("Bandit"));
    }

    /// <summary>
    /// The owner shows the colours its settings pick on its palette and raises them, then follows each change to them; an
    /// unknown swatch, or the choice's own, shows the look's own colours. Once it stops following, a change shows
    /// nothing, and the picks a copy is given show.
    /// </summary>
    [Test]
    public void FollowLocalChoice_ShowsTheSettingsColours_AndEveryChangeToThem()
    {
        UserSettings settings = new(new MemorySaveStore());
        settings.SetCharacterColour("Tunic", "Red");
        PlayerLook look = CreateColouredPlayer(out CharacterPalette palette, out CharacterColours colours);
        look.Construct(settings, null);
        List<CharacterColourPicks> chosen = new();
        look.ColoursChosen += chosen.Add;
        int tunic = colours.IndexOf("Tunic");

        look.FollowLocalChoice();
        Track(palette.Copy);
        Track(palette.Palette);
        Assert.That(chosen.Count, Is.EqualTo(1));
        Assert.That(chosen[0].Get(tunic), Is.EqualTo(1));
        Assert.That(look.ShownColours, Is.EqualTo(chosen[0]));
        Assert.That(palette.IsShown, Is.True);

        settings.SetCharacterColour("Tunic", "Plaid");
        Assert.That(chosen.Count, Is.EqualTo(2));
        Assert.That(chosen[1].IsDefault, Is.True, "An unknown swatch shows the look's own colours.");
        Assert.That(palette.IsShown, Is.False);

        settings.SetCharacterColour("Tunic", "Blue");
        Assert.That(chosen.Count, Is.EqualTo(3));
        Assert.That(chosen[2].IsDefault, Is.True, "The choice's own swatch is no pick.");

        look.StopFollowing();
        settings.SetCharacterColour("Tunic", "Red");
        Assert.That(chosen.Count, Is.EqualTo(3), "Stopped, it no longer follows.");
        Assert.That(palette.IsShown, Is.False);

        look.ShowColours(colours.Pick(default, tunic, "Red"));
        Assert.That(palette.IsShown, Is.True);
        Assert.That(look.ShownColours.Get(tunic), Is.EqualTo(1));
    }

    /// <summary>
    /// The settings keep a swatch per colour choice in their file, which settings read again later find; the same
    /// swatch again neither writes nor tells of a change, an empty one forgets the pick, and a file written before
    /// colours reads with none.
    /// </summary>
    [Test]
    public void TheSettings_KeepTheColourPicks()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        int changes = 0;
        settings.CharacterColoursChanged += () => changes++;
        Assert.That(settings.CharacterColour("Tunic"), Is.Empty);

        Assert.That(settings.SetCharacterColour("Tunic", "Red"), Is.True);
        Assert.That(settings.SetCharacterColour("Hair", "Blonde"), Is.True);
        Assert.That(settings.SetCharacterColour("Tunic", "Red"), Is.False);
        Assert.That(changes, Is.EqualTo(2));
        UserSettings again = new(store);
        Assert.That(again.CharacterColour("Tunic"), Is.EqualTo("Red"));
        Assert.That(again.CharacterColour("Hair"), Is.EqualTo("Blonde"));

        Assert.That(settings.SetCharacterColour("Tunic", ""), Is.True);
        Assert.That(changes, Is.EqualTo(3));
        Assert.That(new UserSettings(store).CharacterColour("Tunic"), Is.Empty);
        Assert.That(new UserSettings(store).CharacterColour("Hair"), Is.EqualTo("Blonde"));

        Assert.That(UserSettingsData.TryParse("{\"version\":1,\"characterLook\":\"Bandit\"}", out UserSettingsData old), Is.True);
        Assert.That(old.CharacterColours, Is.Empty);
    }

    /// <summary>
    /// The adventurer's look shows the outfit its parts pick, composed by its outfits (composed here before it is worn,
    /// so it shows at once), and its own set for its own parts, letting go of the outfit, which is kept as a spare.
    /// </summary>
    [Test]
    public void ShowParts_WearsTheComposedOutfit_AndTheLooksSetForItsOwn()
    {
        using CharacterOutfits outfits = new(2, 700);
        PlayerLook look = CreateAdventurer(outfits, out CharacterVisuals visuals, out CharacterParts parts);
        CharacterPartPicks hood = default(CharacterPartPicks).With(CharacterSlot.Headwear, 1);
        CharacterOutfitLease warm = outfits.WearNow(parts, hood);

        Assert.That(look.Show(0), Is.True);
        Assert.That(visuals.AnimationSet, Is.EqualTo(parts.Look));
        look.ShowParts(hood);
        Assert.That(look.ShownParts, Is.EqualTo(hood));
        Assert.That(visuals.AnimationSet, Is.EqualTo(warm.Set));

        warm.Release();
        look.ShowParts(default);
        Assert.That(visuals.AnimationSet, Is.EqualTo(parts.Look));
        Assert.That(outfits.SpareCount, Is.EqualTo(1), "Nobody wears the hood any more.");
    }

    /// <summary>
    /// The adventurer's weapon slot shows the gear of the weapon shown (Docs/PlayerCharacter.md, Phase 7): the look's own
    /// set with its own sword, the composed outfit with the greatsword on its back, which is not a pick of the player's,
    /// and the option drawing nothing for a weapon without gear or none; the gear stays as the picks change.
    /// </summary>
    [Test]
    public void ShowWeapon_WearsTheWeaponsGear_WhateverThePicks()
    {
        using CharacterOutfits outfits = new(2, 700);
        PlayerLook look = CreateAdventurer(outfits, out CharacterVisuals visuals, out CharacterParts parts);
        PlayerWeapon sword = Track(PlayerWeapon.Create("Sword", 1, 1f, 0.5f, new Vector2[4], gearName: "Sword"));
        PlayerWeapon greatsword = Track(PlayerWeapon.Create("Greatsword", 1, 1f, 0.5f, new Vector2[4], gearName: "Greatsword"));
        PlayerWeapon club = Track(PlayerWeapon.Create("Club", 1, 1f, 0.5f, new Vector2[4]));
        int none = parts.Get(CharacterSlot.Weapon).IndexOf("None");
        CharacterOutfitLease warm = outfits.WearNow(parts, default(CharacterPartPicks).With(CharacterSlot.Weapon, 1));

        Assert.That(look.Show(0), Is.True);
        look.ShowWeapon(sword);
        Assert.That(visuals.AnimationSet, Is.EqualTo(parts.Look), "the sword is the look's own gear");
        look.ShowWeapon(greatsword);
        Assert.That(look.WornParts.Get(CharacterSlot.Weapon), Is.EqualTo(1));
        Assert.That(look.ShownParts.IsDefault, Is.True, "the gear is not a pick");
        Assert.That(visuals.AnimationSet, Is.EqualTo(warm.Set));

        look.ShowParts(default(CharacterPartPicks).With(CharacterSlot.Weapon, 2));
        Assert.That(look.WornParts.Get(CharacterSlot.Weapon), Is.EqualTo(1), "a pick of the weapon's slot changes nothing");
        Assert.That(visuals.AnimationSet, Is.EqualTo(warm.Set));

        look.ShowWeapon(club);
        Assert.That(look.WornParts.Get(CharacterSlot.Weapon), Is.EqualTo(none));
        look.ShowWeapon(null);
        Assert.That(look.WornParts.Get(CharacterSlot.Weapon), Is.EqualTo(none));
        look.ShowWeapon(sword);
        Assert.That(visuals.AnimationSet, Is.EqualTo(parts.Look));
        warm.Release();
    }

    /// <summary>
    /// The owner shows the parts its settings pick of the look shown and raises them, then follows each change; an
    /// unknown option shows the slot's first, and the weapon's slot is never the settings'. The settings keep an option per slot across a restart, an empty one
    /// forgetting the pick.
    /// </summary>
    [Test]
    public void FollowLocalChoice_ShowsTheSettingsParts_WhichTheSettingsKeep()
    {
        using CharacterOutfits outfits = new(2, 700);
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        settings.SetCharacterPart("Cape", "Long");
        settings.SetCharacterPart("Weapon", "Longbow");
        PlayerLook look = CreateAdventurer(outfits, out _, out CharacterParts parts);
        look.Construct(settings, outfits);
        List<CharacterPartPicks> chosen = new();
        look.PartsChosen += chosen.Add;

        look.FollowLocalChoice();
        Assert.That(chosen.Count, Is.EqualTo(1));
        Assert.That(chosen[0].Get(CharacterSlot.Cape), Is.EqualTo(parts.Get(CharacterSlot.Cape).IndexOf("Long")));
        Assert.That(look.ShownParts, Is.EqualTo(chosen[0]));
        Assert.That(chosen[0].Get(CharacterSlot.Weapon), Is.Zero, "the weapon's slot is the weapon's, not the settings'");

        settings.SetCharacterPart("Cape", "Ermine");
        Assert.That(chosen[1].IsDefault, Is.True, "An unknown option shows the first.");
        Assert.That(new UserSettings(store).CharacterPart("Cape"), Is.EqualTo("Ermine"));
        Assert.That(settings.SetCharacterPart("Cape", "Ermine"), Is.False);
        Assert.That(settings.SetCharacterPart("Cape", ""), Is.True);
        Assert.That(new UserSettings(store).CharacterPart("Cape"), Is.Empty);
        look.StopFollowing();
    }

    /// <summary>
    /// A player drawn as the project's adventurer, the only look of a catalog of its own, wearing parts through
    /// <paramref name="outfits"/>.
    /// </summary>
    private PlayerLook CreateAdventurer(CharacterOutfits outfits, out CharacterVisuals visuals, out CharacterParts parts)
    {
        parts = AssetDatabase.LoadAssetAtPath<CharacterParts>("Assets/Sprites/Characters/Adventurer/Parts/Adventurer_Parts.asset");
        CharacterLookCatalog adventurer = Track(CharacterLookCatalog.Create(new CharacterLook("Adventurer", parts.Look, locomotion, parts)));
        PlayerLook look = CreatePlayer(out visuals);
        look.Configure(adventurer, visuals, null, null, outfits);
        return look;
    }

    /// <summary>
    /// A player as <see cref="CreatePlayer"/> makes it, its renderer drawn with the lit sprite material and a palette
    /// beside it, over colours of a skin and a tunic of blue (its own) or red.
    /// </summary>
    private PlayerLook CreateColouredPlayer(out CharacterPalette palette, out CharacterColours colours)
    {
        PlayerLook look = CreatePlayer(out CharacterVisuals visuals);
        visuals.GetComponent<SpriteRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SpritePixelLit.mat");
        palette = visuals.gameObject.AddComponent<CharacterPalette>();
        ColourRamp blue = new(new Color32(20, 30, 50, 255), new Color32(60, 90, 140, 255), new Color32(90, 120, 170, 255), new Color32(140, 170, 210, 255));
        ColourRamp red = new(new Color32(50, 15, 15, 255), new Color32(130, 40, 40, 255), new Color32(170, 55, 50, 255), new Color32(215, 95, 80, 255));
        ColourRamp fair = new(new Color32(90, 50, 40, 255), new Color32(200, 135, 105, 255), new Color32(235, 180, 145, 255), new Color32(250, 215, 185, 255));
        colours = Track(CharacterColours.Create(
            new[] { new CharacterSwatchList("Skin", new[] { new CharacterSwatch("Fair", fair) }), new CharacterSwatchList("Cloth", new[] { new CharacterSwatch("Blue", blue), new CharacterSwatch("Red", red) }) },
            new[] { new CharacterColourChoice("Skin", "Skin", "Fair", CharacterDye.Skin), new CharacterColourChoice("Tunic", "Cloth", "Blue", CharacterDye.Tunic) }));
        look.Configure(catalog, visuals, colours, palette);
        return look;
    }

    /// <summary>
    /// A player with visuals on a child, drawn with the rogue's set, and a look over the catalog.
    /// </summary>
    private PlayerLook CreatePlayer(out CharacterVisuals visuals)
    {
        GameObject player = Track(new GameObject("Player"));
        GameObject child = new("Visuals");
        child.transform.SetParent(player.transform, false);
        child.AddComponent<SpriteRenderer>();
        visuals = child.AddComponent<CharacterVisuals>();
        visuals.SetLook(rogueSet, locomotion);
        PlayerLook look = player.AddComponent<PlayerLook>();
        look.Configure(catalog, visuals);
        return look;
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
