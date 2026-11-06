using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the adventurer's character recipe, the player's base body (Docs/PlayerCharacter.md, Phase 0), on the
/// project's asset, which they only read, and on copies made in memory: baking gives the same pixels every time; the
/// standing adventurer is its target height; its face shows both eyes, their glints, the brows and the mouth from the
/// front, one eye in profile and nothing from behind; its ears show beside the head; its locks grow the hair past the
/// head's ball from the front and in profile; and the outfit's options (short sleeves, gloves, the satchel's strap, the skirt) draw as the recipe sets
/// them.
/// </summary>
public class AdventurerRecipeTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Adventurer.asset";
    private const string IdlePath = "Assets/Settings/Characters/Motions/Motion_Idle.asset";
    private const string WalkPath = "Assets/Settings/Characters/Motions/Motion_Walk.asset";
    private const int East = 0;
    private const int North = 2;
    private const int South = 6;

    private readonly List<Object> created = new();
    private AdventurerRecipe recipe;
    private CharacterBaker baker;
    private CharacterClip idle;
    private CharacterClip walk;

    /// <summary>
    /// Loads the project's adventurer recipe and the idle and walk it is drawn with.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        recipe = AssetDatabase.LoadAssetAtPath<AdventurerRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null);
        baker = new CharacterBaker(recipe);
        idle = Clip("Idle", IdlePath);
        walk = Clip("Walk", WalkPath);
    }

    /// <summary>
    /// Destroys every copy the test made.
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
    /// A new baker of the same recipe gives the same pixels, for the idle and the walk.
    /// </summary>
    [Test]
    public void Bake_SameRecipe_GivesTheSamePixels()
    {
        CharacterBaker again = new(recipe);
        foreach (CharacterClip clip in new[] { idle, walk })
        {
            CharacterSheet first = baker.Bake(clip);
            CharacterSheet second = again.Bake(clip);
            Assert.That(second.Pixels.SequenceEqual(first.Pixels), Is.True, clip.Name);
            Assert.That(first.FramesAtEdge, Is.Empty, clip.Name);
        }
    }

    /// <summary>
    /// The scale makes the standing adventurer the recipe's target height, outline included.
    /// </summary>
    [Test]
    public void StandingAdventurer_IsItsTargetHeight()
    {
        Assert.That(recipe.TargetHeight, Is.EqualTo(40));
        Assert.That(baker.StandingHeight(), Is.InRange(recipe.TargetHeight - 1, recipe.TargetHeight + 1));
    }

    /// <summary>
    /// Facing south the face shows both eyes whole, each with its glint in one pixel, the brows and the mouth; in
    /// profile one eye shows; facing north the face is turned away and none of them is drawn.
    /// </summary>
    [Test]
    public void Face_ShowsItsFeaturesFromTheFront_OneEyeInProfile_AndNoneFromBehind()
    {
        int eye = recipe.EyeWidth * recipe.EyeHeight;
        CharacterCanvas front = baker.Render(walk, South, 0);
        Assert.That(front.Count(CharacterMaterial.Eye) + front.Count(CharacterMaterial.EyeGlint), Is.EqualTo(2 * eye));
        Assert.That(front.Count(CharacterMaterial.EyeGlint), Is.EqualTo(2));
        Assert.That(front.Count(CharacterMaterial.Brow), Is.Positive);
        Assert.That(front.Count(CharacterMaterial.Mouth), Is.EqualTo(Mathf.RoundToInt(recipe.Mouth.y)));

        CharacterCanvas profile = baker.Render(walk, East, 0);
        Assert.That(profile.Count(CharacterMaterial.Eye) + profile.Count(CharacterMaterial.EyeGlint), Is.EqualTo(eye));

        CharacterCanvas back = baker.Render(walk, North, 0);
        foreach (CharacterMaterial feature in new[] { CharacterMaterial.Eye, CharacterMaterial.EyeGlint, CharacterMaterial.Brow, CharacterMaterial.Mouth })
        {
            Assert.That(back.Count(feature), Is.Zero, feature.ToString());
        }
    }

    /// <summary>
    /// The ears show beside the head facing south, and the head's skin is no hair.
    /// </summary>
    [Test]
    public void TheEars_ShowBesideTheHead()
    {
        CharacterCanvas front = baker.Render(idle, South, 0);
        Assert.That(PartPixels(front, CharacterPart.LeftEar), Is.Positive);
        Assert.That(PartPixels(front, CharacterPart.RightEar), Is.Positive);
        Assert.That(front.Count(CharacterMaterial.Skin), Is.Positive);
        Assert.That(front.Count(CharacterMaterial.Hair), Is.Positive);
    }

    /// <summary>
    /// The locks grow the hair past the head's ball: with none, fewer pixels are hair from the front and in profile.
    /// From behind the default style's locks mostly stand within the ball's outline, so north is left out.
    /// </summary>
    [Test]
    public void TheLocks_GrowTheHairPastTheHeadsBall()
    {
        Assert.That(recipe.Locks, Is.Not.Empty);
        CharacterBaker bald = new(Copy("{\"locks\":[]}"));
        foreach (int direction in new[] { East, South })
        {
            Assert.That(bald.Render(idle, direction, 0).Count(CharacterMaterial.Hair), Is.LessThan(baker.Render(idle, direction, 0).Count(CharacterMaterial.Hair)), $"direction {direction}");
        }
    }

    /// <summary>
    /// The outfit's options draw as set: the project's adventurer wears long shirt sleeves, bare hands and no strap;
    /// short sleeves bare the forearms, gloves cover the hands, the strap crosses the chest, and without a skirt less
    /// of the tunic shows.
    /// </summary>
    [Test]
    public void TheOutfitsOptions_DrawAsTheRecipeSetsThem()
    {
        CharacterCanvas plain = baker.Render(idle, South, 0);
        Assert.That(recipe.Sleeves, Is.EqualTo(AdventurerSleeves.Long));
        Assert.That(MaterialOn(plain, CharacterMaterial.Skin, CharacterPart.LeftForearm, CharacterPart.RightForearm), Is.Zero);
        Assert.That(MaterialOn(plain, CharacterMaterial.Skin, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Positive);
        Assert.That(MaterialOn(plain, CharacterMaterial.Belt, CharacterPart.Strap), Is.Zero);

        CharacterCanvas shortSleeves = new CharacterBaker(Copy("{\"sleeves\":1}")).Render(idle, South, 0);
        Assert.That(MaterialOn(shortSleeves, CharacterMaterial.Skin, CharacterPart.LeftForearm, CharacterPart.RightForearm), Is.Positive);
        Assert.That(MaterialOn(shortSleeves, CharacterMaterial.Jerkin, CharacterPart.LeftUpperArm, CharacterPart.RightUpperArm), Is.Positive);

        CharacterCanvas gloved = new CharacterBaker(Copy("{\"gloves\":true}")).Render(idle, South, 0);
        Assert.That(MaterialOn(gloved, CharacterMaterial.Skin, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Zero);
        Assert.That(MaterialOn(gloved, CharacterMaterial.Gloves, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Positive);

        CharacterCanvas strapped = new CharacterBaker(Copy("{\"strap\":true}")).Render(idle, South, 0);
        Assert.That(MaterialOn(strapped, CharacterMaterial.Belt, CharacterPart.Strap), Is.Positive);

        CharacterCanvas skirtless = new CharacterBaker(Copy("{\"skirtLength\":0}")).Render(idle, South, 0);
        Assert.That(skirtless.Count(CharacterMaterial.Jerkin), Is.LessThan(plain.Count(CharacterMaterial.Jerkin)));
    }

    /// <summary>
    /// A clip of the motion at <paramref name="path"/> in the gaits' 64 px frame with the feet on (32, 12).
    /// </summary>
    private static CharacterClip Clip(string name, string path)
    {
        CharacterMotion motion = AssetDatabase.LoadAssetAtPath<CharacterMotion>(path);
        Assert.That(motion, Is.Not.Null, path);
        return new CharacterClip(name, motion, null, new Vector2Int(64, 64), new Vector2Int(32, 12));
    }

    /// <summary>
    /// A copy of the project's recipe with <paramref name="json"/>'s fields overwritten, destroyed after the test.
    /// </summary>
    private AdventurerRecipe Copy(string json)
    {
        AdventurerRecipe copy = Object.Instantiate(recipe);
        created.Add(copy);
        JsonUtility.FromJsonOverwrite(json, copy);
        return copy;
    }

    /// <summary>
    /// How many of <paramref name="canvas"/>'s pixels belong to <paramref name="part"/>.
    /// </summary>
    private static int PartPixels(CharacterCanvas canvas, CharacterPart part)
    {
        return MaterialOn(canvas, null, part);
    }

    /// <summary>
    /// How many of <paramref name="canvas"/>'s pixels belong to one of <paramref name="parts"/> and, when given, are
    /// <paramref name="material"/>.
    /// </summary>
    private static int MaterialOn(CharacterCanvas canvas, CharacterMaterial? material, params CharacterPart[] parts)
    {
        int count = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (canvas.IsFilled(x, y) && parts.Contains(canvas.PartAt(x, y)) && (material == null || canvas.MaterialAt(x, y) == material))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
