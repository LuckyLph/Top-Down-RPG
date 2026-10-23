using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the Weasel's character recipe (Docs/Animation.md, Phase 7) on the project's assets, which they only read:
/// baking gives the same bytes every time and what the sheets hold; every clip has a row per direction and reaches no
/// frame's edge; the standing Weasel is its target height; its face shows both eyes from the front and none from
/// behind; its gaits start with the left foot planted in front and their planted feet move back at their natural
/// speeds, which its speed bands pick at its move speed and slowed; its attack has its contact frame and draws the
/// dagger, which shows in no other clip; the project's animation set holds every clip.
/// </summary>
public class WeaselRecipeTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Weasel.asset";
    private const string LocomotionPath = "Assets/Data/Characters/Locomotion_Weasel.asset";
    private const string ConfigPath = "Assets/Settings/AI/Mob_Default.asset";
    private const int North = 2;
    private const int South = 6;

    private WeaselRecipe recipe;
    private CharacterBaker baker;
    private Dictionary<string, CharacterSheet> sheets;

    /// <summary>
    /// Loads the project's Weasel recipe and bakes each of its clips once for every test.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        recipe = AssetDatabase.LoadAssetAtPath<WeaselRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null);
        baker = new CharacterBaker(recipe);
        sheets = recipe.Clips.ToDictionary(clip => clip.Name, baker.Bake);
    }

    /// <summary>
    /// A new baker of the same recipe gives the same pixels and PNG bytes, clip for clip, and they are the sheets the
    /// project holds.
    /// </summary>
    [Test]
    public void Bake_SameRecipe_GivesTheSameBytes_AsTheProjectsSheets()
    {
        CharacterBaker again = new(recipe);
        foreach (CharacterClip clip in recipe.Clips)
        {
            CharacterSheet first = sheets[clip.Name];
            CharacterSheet second = again.Bake(clip);
            byte[] png = TerrainMaterialWriter.EncodePng(first.Width, first.Height, first.Pixels);
            Assert.That(second.Pixels.SequenceEqual(first.Pixels), Is.True, clip.Name);
            Assert.That(TerrainMaterialWriter.EncodePng(second.Width, second.Height, second.Pixels), Is.EqualTo(png), clip.Name);
            Assert.That(File.ReadAllBytes(CharacterSheetWriter.SheetPath(recipe, clip)), Is.EqualTo(png), clip.Name);
        }
    }

    /// <summary>
    /// Every clip's sheet has 8 rows, a row per direction, and a column per frame of its motion, and nothing it draws
    /// reaches the edge of its frame.
    /// </summary>
    [Test]
    public void EveryClip_HasEightRows_AndFitsItsFrames()
    {
        foreach (CharacterClip clip in recipe.Clips)
        {
            CharacterSheet sheet = sheets[clip.Name];
            Assert.That(sheet.Height / clip.FrameSize.y, Is.EqualTo(8), clip.Name);
            Assert.That(sheet.Width, Is.EqualTo(clip.Motion.FrameCount * clip.FrameSize.x), clip.Name);
            Assert.That(sheet.FramesAtEdge, Is.Empty, clip.Name);
        }
    }

    /// <summary>
    /// The scale makes the standing Weasel exactly its recipe's target height, ears and outline included.
    /// </summary>
    [Test]
    public void StandingWeasel_IsItsTargetHeight()
    {
        Assert.That(baker.StandingHeight(), Is.EqualTo(recipe.TargetHeight));
    }

    /// <summary>
    /// Facing south both eyes show in the band, each its full height; facing north none is drawn.
    /// </summary>
    [Test]
    public void Face_ShowsBothEyesFromTheFront_AndNoneFromBehind()
    {
        CharacterClip idle = recipe.Clips.First(c => c.Name == "Idle");

        Assert.That(baker.Render(idle, South, 0).Count(CharacterMaterial.Eye), Is.EqualTo(2 * recipe.EyeHeight));
        Assert.That(baker.Render(idle, North, 0).Count(CharacterMaterial.Eye), Is.Zero);
        Assert.That(baker.Render(idle, South, 0).Count(CharacterMaterial.Sash), Is.GreaterThan(0), "The band shows.");
        Assert.That(baker.Render(idle, South, 0).Count(CharacterMaterial.FurLight), Is.GreaterThan(0), "So does the pale belly.");
    }

    /// <summary>
    /// The Weasel's gaits start with the left foot planted in front, and while it stays planted it moves back at the
    /// gait's natural speed, at the baker's scale: the walk at 1.2 and the run at 2 units per second, the Weasel's move
    /// speed, east or west.
    /// </summary>
    [Test]
    public void Gaits_StartPlantedAndMoveBackAtTheirNaturalSpeeds()
    {
        Assert.That(Natural("Walk"), Is.EqualTo(1.2f).Within(0.01f));
        Assert.That(Natural("Run"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<MobConfig>(ConfigPath).MoveSpeed).Within(0.01f));
        foreach (string name in new[] { "Walk", "Run" })
        {
            CharacterClip clip = recipe.Clips.First(c => c.Name == name);
            CharacterPose first = clip.Motion.PoseAt(0, recipe, null);
            Assert.That(first.LeftFoot.z, Is.EqualTo(recipe.Ankle).Within(1e-4f), name);
            Assert.That(first.LeftFoot.y, Is.GreaterThan(first.RightFoot.y), name);
            float natural = Natural(name);
            int pairs = 0;
            for (int frame = 0; frame + 1 < clip.Motion.FrameCount; frame++)
            {
                CharacterPose now = clip.Motion.PoseAt(frame, recipe, null);
                CharacterPose next = clip.Motion.PoseAt(frame + 1, recipe, null);
                if (now.LeftFoot.z > recipe.Ankle + 1e-4f || next.LeftFoot.z > recipe.Ankle + 1e-4f)
                {
                    continue;
                }

                float unitsPerSecond = (now.LeftFoot.y - next.LeftFoot.y) / clip.Motion.FrameSeconds(frame) * baker.Scale / CharacterSheetWriter.PixelsPerUnit;
                Assert.That(unitsPerSecond, Is.EqualTo(natural).Within(natural * 1e-3f), $"{name} frame {frame}");
                pairs++;
            }

            Assert.That(pairs, Is.GreaterThan(0), name);
        }
    }

    /// <summary>
    /// The Weasel's speed bands pick the run at its move speed, at its natural rate, and the walk chilled (0.6 of it),
    /// within the playback clamp, so its feet keep up east and west; it never sprints.
    /// </summary>
    [Test]
    public void TheSpeedBands_PickTheRunAtItsMoveSpeed_AndTheWalkChilled()
    {
        CharacterLocomotionSettings settings = AssetDatabase.LoadAssetAtPath<CharacterLocomotionSettings>(LocomotionPath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(recipe));
        float speed = AssetDatabase.LoadAssetAtPath<MobConfig>(ConfigPath).MoveSpeed;
        CharacterLocomotionLayer layer = new(set, settings);

        layer.Advance(speed, 0.1f);
        Assert.That(layer.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(layer.PlaybackRate, Is.EqualTo(1f).Within(0.01f));

        layer.Advance(speed * 0.6f, 0.1f);
        Assert.That(layer.Gait, Is.EqualTo(CharacterGait.Walk));
        Assert.That(layer.PlaybackRate, Is.EqualTo(1f).Within(0.01f));

        layer.Advance(speed * 3f, 0.1f);
        Assert.That(layer.Gait, Is.EqualTo(CharacterGait.Run), "No sprint.");
    }

    /// <summary>
    /// The attack clip has its contact frame (the frames before it the windup, after it the recovery) and draws the
    /// dagger, from its first frame facing south; the idle, walk and run draw no weapon.
    /// </summary>
    [Test]
    public void TheAttack_HasItsContactFrame_AndTheDaggerShowsOnlyInIt()
    {
        CharacterClip slash = recipe.Clips.First(c => c.Name == "DaggerSlash");
        Assert.That(slash.Weapon, Is.Not.Null);
        Assert.That(slash.Motion.ContactFrame, Is.EqualTo(4));
        Assert.That(baker.Render(slash, South, 0).Count(CharacterMaterial.Steel), Is.GreaterThan(0), "The dagger shows from the windup's first frame.");
        Assert.That(baker.Render(slash, South, slash.Motion.ContactFrame).Count(CharacterMaterial.SmearNew), Is.GreaterThan(0), "The contact frame has the slash's smear.");
        foreach (CharacterClip clip in recipe.Clips.Where(c => c.Weapon == null))
        {
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int frame = 0; frame < clip.Motion.FrameCount; frame++)
                {
                    Assert.That(baker.Render(clip, direction, frame).Count(CharacterMaterial.Steel), Is.Zero, $"{clip.Name} {direction} {frame}");
                }
            }
        }
    }

    /// <summary>
    /// The project's animation set holds every clip of the recipe: the unarmed idle, walk and run as its locomotion and
    /// the armed attack with its contact frame, every direction and frame a slice of its sheet.
    /// </summary>
    [Test]
    public void TheProjectsAnimationSet_HoldsEveryClip()
    {
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(recipe));
        Assert.That(set, Is.Not.Null);
        Assert.That(set.Clips.Select(c => c.Name), Is.EqualTo(new[] { "Idle", "Walk", "Run", "DaggerSlash" }));
        Assert.That(set.Locomotion(CharacterGait.Idle).Name, Is.EqualTo("Idle"));
        Assert.That(set.Locomotion(CharacterGait.Walk).Name, Is.EqualTo("Walk"));
        Assert.That(set.Locomotion(CharacterGait.Run).Name, Is.EqualTo("Run"));
        CharacterAnimationClip attack = set.Find("DaggerSlash");
        Assert.That(attack.Armed, Is.True);
        Assert.That(attack.ContactFrame, Is.EqualTo(4));
        foreach (CharacterAnimationClip clip in set.Clips)
        {
            Assert.That(clip.IsComplete, Is.True, clip.Name);
            Assert.That(clip.Armed, Is.EqualTo(clip.Name == "DaggerSlash"), clip.Name);
            Assert.That(AssetDatabase.GetAssetPath(clip.SpriteAt(South, 0)), Does.StartWith("Assets/Sprites/Characters/Weasel/"), clip.Name);
        }
    }

    /// <summary>
    /// The natural speed of the recipe's clip named <paramref name="name"/>.
    /// </summary>
    private float Natural(string name)
    {
        return CharacterSheetWriter.NaturalSpeed(baker, recipe, recipe.Clips.First(c => c.Name == name));
    }
}
