using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the character baker (Docs/Animation.md, Phase 1) on the project's rogue recipe, which they only read:
/// baking gives the same pixels every time; the standing character is the target height; every clip has a row per
/// direction, fits its frames and has its face's eyes only where the face shows; gaits start with the left foot
/// planted in front; a two-handed blade puts the left hand on its grip; easing, the two-bone chains and the recipe's
/// checks behave; and the project's sheets are what the recipe bakes, sliced at 32 pixels per unit with their pivot
/// on the feet.
/// </summary>
public class CharacterRecipeTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const int North = 2;
    private const int South = 6;

    private readonly List<Object> created = new();
    private CharacterRecipe recipe;
    private CharacterBaker baker;
    private Dictionary<string, CharacterSheet> sheets;

    /// <summary>
    /// Loads the project's rogue recipe and bakes each of its clips once for every test.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null);
        baker = new CharacterBaker(recipe);
        sheets = recipe.Clips.ToDictionary(clip => clip.Name, baker.Bake);
    }

    /// <summary>
    /// Destroys every object the test created.
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
    /// A new baker of the same recipe gives the same pixels and PNG bytes, clip for clip.
    /// </summary>
    [Test]
    public void Bake_SameRecipe_GivesTheSameBytes()
    {
        CharacterBaker again = new(recipe);
        foreach (CharacterClip clip in recipe.Clips.Where(c => c.Name == "Run" || c.Name == "SwordSlash"))
        {
            CharacterSheet first = sheets[clip.Name];
            CharacterSheet second = again.Bake(clip);
            Assert.That(second.Pixels.SequenceEqual(first.Pixels), Is.True, clip.Name);
            Assert.That(TerrainMaterialWriter.EncodePng(second.Width, second.Height, second.Pixels), Is.EqualTo(TerrainMaterialWriter.EncodePng(first.Width, first.Height, first.Pixels)), clip.Name);
        }
    }

    /// <summary>
    /// The scale makes the standing character the recipe's target height, outline included.
    /// </summary>
    [Test]
    public void StandingCharacter_IsTheTargetHeight()
    {
        Assert.That(baker.StandingHeight(), Is.InRange(recipe.TargetHeight - 1, recipe.TargetHeight + 1));
    }

    /// <summary>
    /// Every clip's sheet has a row per direction and a column per frame of its motion, and nothing it draws reaches
    /// the edge of its frame.
    /// </summary>
    [Test]
    public void EveryClip_HasARowPerDirection_AndFitsItsFrames()
    {
        foreach (CharacterClip clip in recipe.Clips)
        {
            CharacterSheet sheet = sheets[clip.Name];
            Assert.That(sheet.Width, Is.EqualTo(clip.Motion.FrameCount * clip.FrameSize.x), clip.Name);
            Assert.That(sheet.Height, Is.EqualTo(CharacterDirections.Count * clip.FrameSize.y), clip.Name);
            Assert.That(sheet.FramesAtEdge, Is.Empty, clip.Name);
        }
    }

    /// <summary>
    /// Facing south the face shows both eyes, each its full height; facing north the hood hides the face and no eye
    /// is drawn.
    /// </summary>
    [Test]
    public void Face_ShowsBothEyesFromTheFront_AndNoneFromBehind()
    {
        CharacterClip walk = recipe.Clips.First(c => c.Name == "Walk");

        Assert.That(baker.Render(walk, South, 0).Count(CharacterMaterial.Eye), Is.EqualTo(2 * recipe.EyeHeight));
        Assert.That(baker.Render(walk, North, 0).Count(CharacterMaterial.Eye), Is.Zero);
        Assert.That(baker.Render(walk, North, 0).Count(CharacterMaterial.Face), Is.Zero);
    }

    /// <summary>
    /// Every gait's first frame has the left foot planted on the ground in front of the right one, so switching gaits
    /// keeps the place in the stride.
    /// </summary>
    [Test]
    public void Gaits_StartWithTheLeftFootPlantedInFront()
    {
        IEnumerable<GaitMotion> gaits = recipe.Clips.Select(c => c.Motion).OfType<GaitMotion>().Distinct();
        Assert.That(gaits, Is.Not.Empty);
        foreach (GaitMotion gait in gaits)
        {
            CharacterPose pose = gait.PoseAt(0, recipe, null);
            Assert.That(pose.LeftFoot.z, Is.EqualTo(recipe.Ankle).Within(1e-4f), gait.name);
            Assert.That(pose.LeftFoot.y, Is.GreaterThan(pose.RightFoot.y), gait.name);
        }
    }

    /// <summary>
    /// With a two-handed blade the left hand holds the grip below the right one in every frame of the greatsword's
    /// attack.
    /// </summary>
    [Test]
    public void TwoHandedBlade_PutsTheLeftHandOnTheGrip()
    {
        CharacterClip cleave = recipe.Clips.First(c => c.Name == "GreatswordCleave");
        CharacterWeaponArt greatsword = cleave.Weapon;
        Assert.That(greatsword.TwoHanded, Is.True);
        for (int frame = 0; frame < cleave.Motion.FrameCount; frame++)
        {
            CharacterPose pose = cleave.Motion.PoseAt(frame, recipe, greatsword);
            CharacterJoints joints = CharacterRig.Solve(recipe, pose, greatsword);
            Vector3 grip = joints.RightHand - pose.Blade.normalized * greatsword.LeftGripOffset;
            Assert.That(Vector3.Distance(joints.LeftHand, grip), Is.LessThan(0.75f), $"frame {frame}");
        }
    }

    /// <summary>
    /// Each easing starts at 0 and ends at 1; in-out is half way at the middle, out ahead of it and in behind it; a
    /// step holds until the end.
    /// </summary>
    [Test]
    public void Ease_FollowsEachCurve()
    {
        foreach (MotionEasing easing in System.Enum.GetValues(typeof(MotionEasing)))
        {
            Assert.That(KeyedMotion.Ease(easing, 0f), Is.EqualTo(0f).Within(1e-6f), easing.ToString());
            Assert.That(KeyedMotion.Ease(easing, 1f), Is.EqualTo(1f).Within(1e-6f), easing.ToString());
        }

        Assert.That(KeyedMotion.Ease(MotionEasing.InOut, 0.5f), Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(KeyedMotion.Ease(MotionEasing.Out, 0.5f), Is.EqualTo(0.75f).Within(1e-6f));
        Assert.That(KeyedMotion.Ease(MotionEasing.In, 0.5f), Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(KeyedMotion.Ease(MotionEasing.Linear, 0.3f), Is.EqualTo(0.3f).Within(1e-6f));
        Assert.That(KeyedMotion.Ease(MotionEasing.Step, 0.99f), Is.Zero);
    }

    /// <summary>
    /// A two-bone chain reaches a target within reach with both bones at their length, bending toward its pole, and
    /// straightens toward one beyond reach.
    /// </summary>
    [Test]
    public void Reach_BendsToATargetInReach_AndStraightensTowardOneBeyond()
    {
        Vector3 root = new(0f, 0f, 10f);
        CharacterRig.Reach(root, new Vector3(0f, 0f, 2f), 5f, 5f, new Vector3(0f, 1f, 0f), out Vector3 knee, out Vector3 foot);
        Assert.That(Vector3.Distance(foot, new Vector3(0f, 0f, 2f)), Is.LessThan(1e-4f));
        Assert.That(Vector3.Distance(root, knee), Is.EqualTo(5f).Within(1e-4f));
        Assert.That(Vector3.Distance(knee, foot), Is.EqualTo(5f).Within(1e-4f));
        Assert.That(knee.y, Is.GreaterThan(0f), "The knee bends toward its pole.");

        CharacterRig.Reach(root, new Vector3(0f, 0f, -20f), 5f, 5f, new Vector3(0f, 1f, 0f), out _, out Vector3 reached);
        Assert.That(reached.z, Is.EqualTo(0f).Within(0.01f));
        Assert.That(reached.x, Is.EqualTo(0f).Within(1e-4f));
    }

    /// <summary>
    /// A recipe cannot bake with a sheet name that is not letters and digits, two clips of one name, or a pivot
    /// outside its frame.
    /// </summary>
    [Test]
    public void CanGenerate_RefusesBadNamesAndFrames()
    {
        Assert.That(recipe.CanGenerate(out string problem), Is.True, problem);

        CharacterRecipe badName = Track(Object.Instantiate(recipe));
        SetString(badName, "sheetName", "Rogue 2");
        Assert.That(badName.CanGenerate(out _), Is.False);

        CharacterRecipe twice = Track(Object.Instantiate(recipe));
        SerializedObject serialized = new(twice);
        serialized.FindProperty("clips").GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue = recipe.Clips[0].Name;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(twice.CanGenerate(out _), Is.False);

        CharacterRecipe outside = Track(Object.Instantiate(recipe));
        serialized = new SerializedObject(outside);
        serialized.FindProperty("clips").GetArrayElementAtIndex(0).FindPropertyRelative("pivot").vector2IntValue = new Vector2Int(200, 12);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(outside.CanGenerate(out _), Is.False);
    }

    /// <summary>
    /// Each of the project's sheets is what the recipe bakes, imported as sprites at 32 pixels per unit, sliced into
    /// one sprite per direction and frame named after the character, clip, direction and frame, at its frame's rect
    /// with its pivot on the clip's feet pixel.
    /// </summary>
    [Test]
    public void TheProjectsSheets_AreWhatTheRecipeBakes_SlicedPerDirectionAndFrame()
    {
        foreach (CharacterClip clip in recipe.Clips)
        {
            string path = CharacterSheetWriter.SheetPath(recipe, clip);
            CharacterSheet sheet = sheets[clip.Name];
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(TerrainMaterialWriter.EncodePng(sheet.Width, sheet.Height, sheet.Pixels)), path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(CharacterSheetWriter.PixelsPerUnit), path);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple), path);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);

            Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            Assert.That(sprites.Count, Is.EqualTo(CharacterDirections.Count * sheet.FrameCount), path);
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int frame = 0; frame < sheet.FrameCount; frame++)
                {
                    string name = CharacterSheetWriter.SliceName(recipe, clip, direction, frame);
                    Assert.That(sprites.TryGetValue(name, out Sprite sprite), Is.True, name);
                    RectInt rect = CharacterSheet.FrameRect(direction, frame, clip.FrameSize);
                    Assert.That(sprite.rect, Is.EqualTo(new Rect(rect.x, rect.y, rect.width, rect.height)), name);
                    Assert.That(sprite.pivot, Is.EqualTo((Vector2)clip.Pivot), name);
                    Assert.That(sprite.pixelsPerUnit, Is.EqualTo(CharacterSheetWriter.PixelsPerUnit), name);
                }
            }
        }
    }

    /// <summary>
    /// Sets a string field of <paramref name="target"/> through its serialized object.
    /// </summary>
    private static void SetString(Object target, string field, string value)
    {
        SerializedObject serialized = new(target);
        serialized.FindProperty(field).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
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
