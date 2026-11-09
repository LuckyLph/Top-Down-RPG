using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for a player's colours (Docs/PlayerCharacter.md, Phase 5): picks keep a swatch per choice, a byte each, the
/// default picking every choice's own; picking a swatch by name keeps none for the choice's own or an unknown name; a
/// palette gives each choice's dyes the ramps of the swatch it shows, opaque, and leaves the dyes no choice colours
/// clear; and the project's colours colour every dye the adventurer's recipe gives a material, once, each choice's own
/// swatch the colours the adventurer is baked in, with no more choices than picks carry, the nine its sheets carry
/// among them.
/// </summary>
public class CharacterColoursTests
{
    private const string ColoursPath = "Assets/Data/Characters/CharacterColours.asset";
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Adventurer.asset";
    private const string SetPath = "Assets/Sprites/Characters/Adventurer/Adventurer_AnimationSet.asset";

    private static readonly ColourRamp Blue = Ramp(20, 60, 160);
    private static readonly ColourRamp Red = Ramp(160, 40, 30);
    private static readonly ColourRamp Fair = Ramp(230, 180, 140);
    private static readonly ColourRamp Lips = Ramp(170, 90, 80);

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every set of colours the test made.
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
    /// The default picks every choice's own; a pick for each of the sixteen choices reads back alone, -1 puts the
    /// choice's own back, and a choice or swatch past what a byte carries changes nothing.
    /// </summary>
    [Test]
    public void Picks_KeepASwatchPerChoice()
    {
        CharacterColourPicks picks = default;
        Assert.That(picks.IsDefault, Is.True);
        for (int choice = 0; choice < CharacterColourPicks.MaxChoices; choice++)
        {
            Assert.That(picks.Get(choice), Is.EqualTo(-1));
        }

        for (int choice = 0; choice < CharacterColourPicks.MaxChoices; choice++)
        {
            CharacterColourPicks one = picks.With(choice, choice * 10);
            Assert.That(one.IsDefault, Is.False);
            for (int other = 0; other < CharacterColourPicks.MaxChoices; other++)
            {
                Assert.That(one.Get(other), Is.EqualTo(other == choice ? choice * 10 : -1), $"{choice} {other}");
            }

            Assert.That(one.With(choice, -1), Is.EqualTo(picks));
        }

        CharacterColourPicks full = picks.With(15, CharacterColourPicks.MaxSwatches - 1).With(0, 0);
        Assert.That(full.Get(15), Is.EqualTo(CharacterColourPicks.MaxSwatches - 1));
        Assert.That(full.Get(0), Is.EqualTo(0));
        Assert.That(full.With(CharacterColourPicks.MaxChoices, 1), Is.EqualTo(full));
        Assert.That(full.With(1, CharacterColourPicks.MaxSwatches), Is.EqualTo(full));
        Assert.That(full.With(1, -2), Is.EqualTo(full));
        Assert.That(full.Get(CharacterColourPicks.MaxChoices), Is.EqualTo(-1));
        Assert.That(full.Get(-1), Is.EqualTo(-1));
    }

    /// <summary>
    /// Picking a swatch by name keeps its index in the choice's list; the choice's own swatch, an unknown name and an
    /// empty one keep none, so picks of the look's own colours are the default.
    /// </summary>
    [Test]
    public void Pick_KeepsTheNamedSwatch_AndNoneForTheChoicesOwn()
    {
        CharacterColours colours = Colours();
        int tunic = colours.IndexOf("Tunic");
        Assert.That(tunic, Is.EqualTo(1));
        Assert.That(colours.IndexOf("Cape"), Is.EqualTo(-1));
        Assert.That(colours.OwnIndex(tunic), Is.EqualTo(0));

        CharacterColourPicks red = colours.Pick(default, tunic, "Red");
        Assert.That(red.Get(tunic), Is.EqualTo(1));
        Assert.That(colours.Shown(red, tunic).Name, Is.EqualTo("Red"));
        Assert.That(colours.Pick(red, tunic, "Blue").IsDefault, Is.True, "The choice's own swatch picks none.");
        Assert.That(colours.Pick(red, tunic, "Plaid").IsDefault, Is.True, "An unknown swatch picks none.");
        Assert.That(colours.Pick(red, tunic, "").IsDefault, Is.True);
        Assert.That(colours.Shown(default, tunic).Name, Is.EqualTo("Blue"));
    }

    /// <summary>
    /// A palette gives each choice's dyes, in order, the ramps of the swatch it shows, its own by default, opaque even
    /// from a clear ramp, a row of tones per dye; a dye no choice colours stays clear.
    /// </summary>
    [Test]
    public void WritePalette_GivesEachChoicesDyesItsSwatchsRamps()
    {
        CharacterColours colours = Colours();
        Color32[] pixels = new Color32[CharacterPaletteMap.PaletteSize];

        colours.WritePalette(default, pixels);
        ExpectRow(pixels, CharacterDye.Skin, Fair);
        ExpectRow(pixels, CharacterDye.Mouth, Lips);
        ExpectRow(pixels, CharacterDye.Tunic, Blue);
        ExpectRow(pixels, CharacterDye.Shirt, Blue);
        Assert.That(pixels[CharacterPaletteMap.Code(CharacterDye.Boots, 2)], Is.EqualTo(new Color32(0, 0, 0, 0)), "No choice colours the boots.");
        Assert.That(pixels[CharacterPaletteMap.Code(CharacterDye.Tunic, 0)].a, Is.EqualTo(255));

        colours.WritePalette(colours.Pick(default, colours.IndexOf("Tunic"), "Red"), pixels);
        ExpectRow(pixels, CharacterDye.Tunic, Red);
        ExpectRow(pixels, CharacterDye.Shirt, Blue);
    }

    /// <summary>
    /// The project's colours carry at most as many choices as picks do; each choice's list holds its own swatch, every
    /// swatch has a ramp for each of its choice's dyes and a name of its own in its list; every dye the adventurer's
    /// recipe gives a material is coloured by exactly one choice, whose own swatch gives it the ramp the adventurer is
    /// baked in, so the default palette is the baked colours; and of the choices, the adventurer's sheets carry the nine
    /// from the skin to the buckle, not the gloves, the strap or the pouch its outfit does not show.
    /// </summary>
    [Test]
    public void TheProjectsColours_ColourEveryDyeOfTheAdventurer_InTheColoursItIsBakedIn()
    {
        CharacterColours colours = AssetDatabase.LoadAssetAtPath<CharacterColours>(ColoursPath);
        AdventurerRecipe recipe = AssetDatabase.LoadAssetAtPath<AdventurerRecipe>(RecipePath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        Assert.That(colours, Is.Not.Null);
        Assert.That(colours.Count, Is.InRange(1, CharacterColourPicks.MaxChoices));

        Dictionary<CharacterDye, int> colouredBy = new();
        for (int c = 0; c < colours.Count; c++)
        {
            CharacterColourChoice choice = colours.Get(c);
            Assert.That(colours.OwnIndex(c), Is.GreaterThanOrEqualTo(0), choice.Name);
            IReadOnlyList<CharacterSwatch> swatches = colours.Swatches(c);
            Assert.That(swatches.Select(swatch => swatch.Name), Is.Unique, choice.Name);
            foreach (CharacterSwatch swatch in swatches)
            {
                Assert.That(swatch.Ramps.Count, Is.GreaterThanOrEqualTo(choice.Dyes.Count), $"{choice.Name} {swatch.Name}");
            }

            foreach (CharacterDye dye in choice.Dyes)
            {
                Assert.That(colouredBy.ContainsKey(dye), Is.False, $"{dye} is coloured twice");
                colouredBy[dye] = c;
            }
        }

        Color32[] palette = new Color32[CharacterPaletteMap.PaletteSize];
        colours.WritePalette(default, palette);
        int dyed = 0;
        for (int m = 0; m < CharacterMaterials.Count; m++)
        {
            CharacterMaterial material = (CharacterMaterial)m;
            CharacterDye dye = recipe.DyeOf(material);
            if (dye == CharacterDye.None)
            {
                continue;
            }

            dyed++;
            Assert.That(colouredBy.ContainsKey(dye), Is.True, $"{material}'s dye {dye} has no choice");
            for (int tone = 0; tone < ColourRamp.ToneCount; tone++)
            {
                Assert.That(palette[CharacterPaletteMap.Code(dye, tone)], Is.EqualTo(recipe.Ramp(material).Tone(tone)), $"{material} tone {tone}");
            }
        }

        Assert.That(dyed, Is.EqualTo(16));
        string[] carried = Enumerable.Range(0, colours.Count).Where(c => colours.Colours(c, set.Dyes)).Select(c => colours.Get(c).Name).ToArray();
        Assert.That(carried, Is.EqualTo(new[] { "Skin", "Hair", "Eyes", "Tunic", "Shirt", "Trousers", "Boots", "Belt", "Buckle" }));
    }

    /// <summary>
    /// Asserts that <paramref name="dye"/>'s row of <paramref name="pixels"/> is <paramref name="ramp"/>, opaque.
    /// </summary>
    private static void ExpectRow(Color32[] pixels, CharacterDye dye, ColourRamp ramp)
    {
        for (int tone = 0; tone < ColourRamp.ToneCount; tone++)
        {
            Color32 expected = ramp.Tone(tone);
            expected.a = 255;
            Assert.That(pixels[CharacterPaletteMap.Code(dye, tone)], Is.EqualTo(expected), $"{dye} tone {tone}");
        }
    }

    /// <summary>
    /// Colours of three choices: the skin and the mouth from a list of skins, then the tunic and the shirt from one list
    /// of cloths, blue their own.
    /// </summary>
    private CharacterColours Colours()
    {
        CharacterSwatchList skins = new("Skin", new[] { new CharacterSwatch("Fair", Fair, Lips) });
        CharacterSwatchList cloths = new("Cloth", new[] { new CharacterSwatch("Blue", Blue), new CharacterSwatch("Red", Red) });
        CharacterColours colours = CharacterColours.Create(new[] { skins, cloths }, new[]
        {
            new CharacterColourChoice("Skin", "Skin", "Fair", CharacterDye.Skin, CharacterDye.Mouth),
            new CharacterColourChoice("Tunic", "Cloth", "Blue", CharacterDye.Tunic),
            new CharacterColourChoice("Shirt", "Cloth", "Blue", CharacterDye.Shirt)
        });
        created.Add(colours);
        return colours;
    }

    /// <summary>
    /// A ramp around a middle colour, its outline's alpha clear, so writing a palette has to make it opaque.
    /// </summary>
    private static ColourRamp Ramp(byte r, byte g, byte b)
    {
        return new ColourRamp(new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), 0), new Color32((byte)(r * 3 / 4), (byte)(g * 3 / 4), (byte)(b * 3 / 4), 255), new Color32(r, g, b, 255), new Color32((byte)((r + 255) / 2), (byte)((g + 255) / 2), (byte)((b + 255) / 2), 255));
    }
}
