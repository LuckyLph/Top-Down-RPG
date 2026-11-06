using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the adventurer's character recipe, the player's base body (Docs/PlayerCharacter.md, Phases 0 and 1), on
/// the project's asset, which they only read, and on copies made in memory: baking gives the same pixels every time; the
/// standing adventurer is its target height; its face shows both eyes, their glints, the brows and the mouth from the
/// front, one eye in profile and nothing from behind; its ears show beside the head; its locks grow the hair past the
/// head's ball from the front and in profile; the outfit's options (short sleeves, gloves, the satchel's strap, the
/// skirt) draw as the recipe sets them; every clip has a row per direction and fits its frames; the rogue's attacks and
/// casts fit its body; and the project's sheets and animation set are what the recipe bakes.
/// </summary>
public class AdventurerRecipeTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Adventurer.asset";
    private const string RoguePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const int East = 0;
    private const int North = 2;
    private const int South = 6;

    private readonly List<Object> created = new();
    private AdventurerRecipe recipe;
    private CharacterBaker baker;
    private CharacterClip idle;
    private CharacterClip walk;
    private Dictionary<string, CharacterSheet> sheets;

    /// <summary>
    /// Every clip of the recipe baked once, on first use.
    /// </summary>
    private Dictionary<string, CharacterSheet> Sheets => sheets ??= recipe.Clips.ToDictionary(clip => clip.Name, baker.Bake);

    /// <summary>
    /// Loads the project's adventurer recipe and its idle and walk.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        recipe = AssetDatabase.LoadAssetAtPath<AdventurerRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null);
        baker = new CharacterBaker(recipe);
        idle = recipe.Clips.FirstOrDefault(clip => clip.Name == "Idle");
        walk = recipe.Clips.FirstOrDefault(clip => clip.Name == "Walk");
        Assert.That(idle, Is.Not.Null);
        Assert.That(walk, Is.Not.Null);
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
    /// Every clip's sheet has a row per direction of its frames, and no frame is drawn to its edge.
    /// </summary>
    [Test]
    public void EveryClip_HasARowPerDirection_AndFitsItsFrames()
    {
        Assert.That(recipe.CanGenerate(out string problem), Is.True, problem);
        foreach (CharacterClip clip in recipe.Clips)
        {
            CharacterSheet sheet = Sheets[clip.Name];
            Assert.That(sheet.Height, Is.EqualTo(CharacterDirections.Count * clip.FrameSize.y), clip.Name);
            Assert.That(sheet.Width, Is.EqualTo(clip.Motion.FrameCount * clip.FrameSize.x), clip.Name);
            Assert.That(sheet.FramesAtEdge, Is.Empty, clip.Name);
        }
    }

    /// <summary>
    /// The rogue's attacks and casts, keyed on the rogue's body, fit the adventurer's (<see cref="KeyedMotion.FitToBody"/>):
    /// in every frame the pelvis's height over the ankles and each foot's height over them are the same share of the
    /// legs' length, so a planted foot stays on the ground, and each hand is as far from its shoulder in arm lengths as on
    /// the rogue. On a copy of the rogue's body nothing changes.
    /// </summary>
    [Test]
    public void TheRoguesKeyedMotions_FitTheAdventurersBody()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RoguePath);
        CharacterRecipe rogueCopy = Object.Instantiate(rogue);
        created.Add(rogueCopy);
        float legs = (recipe.Thigh + recipe.Shin) / (rogue.Thigh + rogue.Shin);
        float rogueArm = rogue.UpperArm + rogue.Forearm;
        float arm = recipe.UpperArm + recipe.Forearm;
        int motions = 0;
        foreach (CharacterClip clip in recipe.Clips)
        {
            if (clip.Motion is not KeyedMotion motion)
            {
                continue;
            }

            motions++;
            Assert.That(motion.PosedOn, Is.EqualTo(rogue), clip.Name);
            for (int frame = 0; frame < motion.FrameCount; frame++)
            {
                string name = $"{clip.Name} {frame}";
                CharacterPose keyed = motion.PoseAt(frame, rogue, clip.Weapon);
                CharacterPose fitted = motion.PoseAt(frame, recipe, clip.Weapon);
                CharacterPose onCopy = motion.PoseAt(frame, rogueCopy, clip.Weapon);
                Assert.That(onCopy.PelvisHeight, Is.EqualTo(keyed.PelvisHeight), name);
                Assert.That(onCopy.LeftHand, Is.EqualTo(keyed.LeftHand), name);
                Assert.That(onCopy.RightFoot, Is.EqualTo(keyed.RightFoot), name);

                Assert.That(fitted.PelvisHeight - recipe.Ankle, Is.EqualTo((keyed.PelvisHeight - rogue.Ankle) * legs).Within(1e-4f), name);
                Assert.That(fitted.LeftFoot.z - recipe.Ankle, Is.EqualTo((keyed.LeftFoot.z - rogue.Ankle) * legs).Within(1e-4f), name);
                Assert.That(fitted.RightFoot.z - recipe.Ankle, Is.EqualTo((keyed.RightFoot.z - rogue.Ankle) * legs).Within(1e-4f), name);
                CharacterJoints keyedJoints = CharacterRig.Solve(rogue, keyed, clip.Weapon);
                CharacterJoints fittedJoints = CharacterRig.Solve(recipe, fitted, clip.Weapon);
                Assert.That(Vector3.Distance(fitted.LeftHand, fittedJoints.LeftShoulder) / arm, Is.EqualTo(Vector3.Distance(keyed.LeftHand, keyedJoints.LeftShoulder) / rogueArm).Within(1e-4f), name);
                Assert.That(Vector3.Distance(fitted.RightHand, fittedJoints.RightShoulder) / arm, Is.EqualTo(Vector3.Distance(keyed.RightHand, keyedJoints.RightShoulder) / rogueArm).Within(1e-4f), name);
            }
        }

        Assert.That(motions, Is.EqualTo(13), "The weapons' attacks and the standing casts.");
    }

    /// <summary>
    /// Each of the project's adventurer sheets is what the recipe bakes, sliced per direction and frame at 32 pixels per
    /// unit with its pivot on the feet (<see cref="CharacterSheetAssert.SheetsAreWhatTheRecipeBakes"/>).
    /// </summary>
    [Test]
    public void TheProjectsSheets_AreWhatTheRecipeBakes_SlicedPerDirectionAndFrame()
    {
        CharacterSheetAssert.SheetsAreWhatTheRecipeBakes(recipe, Sheets);
    }

    /// <summary>
    /// The project's adventurer animation set holds every clip the recipe bakes, in order
    /// (<see cref="CharacterSheetAssert.SetHoldsWhatTheRecipeBakes"/>).
    /// </summary>
    [Test]
    public void TheProjectsAnimationSet_HoldsWhatTheRecipeBakes()
    {
        CharacterSheetAssert.SetHoldsWhatTheRecipeBakes(recipe, baker);
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
