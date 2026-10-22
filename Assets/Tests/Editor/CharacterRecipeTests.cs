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
/// planted in front, and a planted foot moves back at the gait's natural speed; the idle keeps both feet planted; a
/// two-handed blade puts the left hand on its grip; easing, the two-bone chains and the recipe's checks behave; the
/// project's sheets are what the recipe bakes, sliced at 32 pixels per unit with their pivot on the feet; the baker
/// writes each attack's contact frame into the set; and the project's animation set holds every clip's sprites, frame
/// durations, gait, natural speed and contact frame.
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
    /// While a gait's left foot stays planted from one frame to the next it moves back by the gait's natural speed over
    /// the frame's duration, at the baker's scale: played at its natural rate a character moving at that speed east
    /// or west keeps its feet where they stand. The gaits get faster from walk to run to sprint.
    /// </summary>
    [Test]
    public void Gaits_PlantedFootMovesBackAtTheNaturalSpeed()
    {
        float previous = 0f;
        foreach (CharacterGait gaitKind in new[] { CharacterGait.Walk, CharacterGait.Run, CharacterGait.Sprint })
        {
            CharacterClip clip = recipe.Clips.First(c => c.Weapon == null && c.Motion.Gait == gaitKind);
            float natural = CharacterSheetWriter.NaturalSpeed(baker, recipe, clip);
            Assert.That(natural, Is.GreaterThan(previous), clip.Name);
            previous = natural;
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
                Assert.That(unitsPerSecond, Is.EqualTo(natural).Within(natural * 1e-3f), $"{clip.Name} frame {frame}");
                pairs++;
            }

            Assert.That(pairs, Is.GreaterThan(0), $"{clip.Name} should keep its left foot planted across at least two frames.");
        }
    }

    /// <summary>
    /// The idle keeps both feet planted in every frame and does not travel.
    /// </summary>
    [Test]
    public void Idle_KeepsBothFeetPlanted()
    {
        CharacterClip idle = recipe.Clips.First(c => c.Weapon == null && c.Motion.Gait == CharacterGait.Idle);
        Assert.That(CharacterSheetWriter.NaturalSpeed(baker, recipe, idle), Is.Zero);
        for (int frame = 0; frame < idle.Motion.FrameCount; frame++)
        {
            CharacterPose pose = idle.Motion.PoseAt(frame, recipe, null);
            CharacterJoints joints = CharacterRig.Solve(recipe, pose, null);
            Assert.That(joints.LeftAnkle.z, Is.EqualTo(recipe.Ankle).Within(1e-3f), $"frame {frame}");
            Assert.That(joints.RightAnkle.z, Is.EqualTo(recipe.Ankle).Within(1e-3f), $"frame {frame}");
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
    /// The project's animation set holds every clip of the recipe in order: its name, its motion's gait, whether it is
    /// drawn with a weapon, its motion's frame durations, its natural speed, its motion's contact frame (none for a
    /// gait), and for every direction and frame the sheet's slice of that name. Its locomotion is the unarmed Idle, Walk,
    /// Run and Sprint.
    /// </summary>
    [Test]
    public void TheProjectsAnimationSet_HoldsWhatTheRecipeBakes()
    {
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(recipe));
        Assert.That(set, Is.Not.Null);
        Assert.That(set.Clips.Count, Is.EqualTo(recipe.Clips.Count));
        for (int i = 0; i < recipe.Clips.Count; i++)
        {
            CharacterClip clip = recipe.Clips[i];
            CharacterAnimationClip baked = set.Clips[i];
            Assert.That(baked.Name, Is.EqualTo(clip.Name));
            Assert.That(baked.Gait, Is.EqualTo(clip.Motion.Gait), clip.Name);
            Assert.That(baked.Armed, Is.EqualTo(clip.Weapon != null), clip.Name);
            Assert.That(baked.IsComplete, Is.True, clip.Name);
            Assert.That(baked.FrameCount, Is.EqualTo(clip.Motion.FrameCount), clip.Name);
            Assert.That(baked.NaturalSpeed, Is.EqualTo(CharacterSheetWriter.NaturalSpeed(baker, recipe, clip)).Within(1e-5f), clip.Name);
            Assert.That(baked.ContactFrame, Is.EqualTo(clip.Motion.ContactFrame), clip.Name);
            Assert.That(baked.HasContact, Is.EqualTo(clip.Motion is KeyedMotion), clip.Name);
            string sheetPath = CharacterSheetWriter.SheetPath(recipe, clip);
            for (int frame = 0; frame < clip.Motion.FrameCount; frame++)
            {
                Assert.That(baked.FrameSeconds(frame), Is.EqualTo(clip.Motion.FrameSeconds(frame)), $"{clip.Name} {frame}");
                for (int direction = 0; direction < CharacterDirections.Count; direction++)
                {
                    Sprite sprite = baked.SpriteAt(direction, frame);
                    Assert.That(sprite, Is.Not.Null, $"{clip.Name} {direction} {frame}");
                    Assert.That(sprite.name, Is.EqualTo(CharacterSheetWriter.SliceName(recipe, clip, direction, frame)));
                    Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(sheetPath));
                }
            }
        }

        Assert.That(set.Locomotion(CharacterGait.Idle).Name, Is.EqualTo("Idle"));
        Assert.That(set.Locomotion(CharacterGait.Walk).Name, Is.EqualTo("Walk"));
        Assert.That(set.Locomotion(CharacterGait.Run).Name, Is.EqualTo("Run"));
        Assert.That(set.Locomotion(CharacterGait.Sprint).Name, Is.EqualTo("Sprint"));
    }

    /// <summary>
    /// The baker writes each keyed motion's contact frame into the set it builds, so the frames before it are the
    /// attack's windup and those after it its recovery (the sword's slash: frames 0 to 3, 4, then 5 to 7), and no
    /// contact frame for a gait or the idle.
    /// </summary>
    [Test]
    public void TheBaker_WritesTheContactFrameIntoTheSet()
    {
        CharacterAnimationSet built = CharacterSheetWriter.BuildSet(recipe, baker);
        try
        {
            CharacterAnimationClip slash = built.Find("SwordSlash");
            KeyedMotion motion = (KeyedMotion)recipe.Clips.First(c => c.Name == "SwordSlash").Motion;
            Assert.That(slash.ContactFrame, Is.EqualTo(motion.ContactFrame));
            Assert.That(slash.ContactFrame, Is.EqualTo(4));
            Assert.That(slash.WindupSeconds, Is.EqualTo(motion.FrameSeconds(0) + motion.FrameSeconds(1) + motion.FrameSeconds(2) + motion.FrameSeconds(3)).Within(1e-6f));
            Assert.That(slash.RecoverySeconds, Is.EqualTo(motion.FrameSeconds(5) + motion.FrameSeconds(6) + motion.FrameSeconds(7)).Within(1e-6f));
            Assert.That(built.Find("GreatswordCleave").HasContact, Is.True);
            Assert.That(built.Find("BowShot").HasContact, Is.True);
            Assert.That(built.Find("Run").ContactFrame, Is.EqualTo(-1));
            Assert.That(built.Find("Idle").HasContact, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(built);
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
