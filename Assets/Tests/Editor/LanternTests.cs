using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the lantern every player carries (Docs/Lighting.md, Phase 9): the animator showing each frame's lantern
/// layer and light, held with its sprite and over a gait; the visuals drawing the layer and casting its light only while
/// it burns, beside a cast's light; showing it allocating nothing; the owner's lantern lighting itself at sunset and
/// going out at sunrise, the key's choice holding until the next of those, and a copy showing its owner's choice; its
/// light flickering within its share; the rogue's set carrying it on every frame as the baker draws it, hanging plumb
/// from the left hand or, while a weapon takes that hand, at the hip; the layer over a sheet being the frame drawn with
/// the lantern, glowing at its glass; the layer packed tightly with its slices pivoted on the feet and written with its
/// glow; a hit's flash covering it with the body; the player prefab carrying it; and the lantern key, rebindable,
/// reaching the local player's lantern.
/// </summary>
public class LanternTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const string RogueSetPath = "Assets/Sprites/Characters/Rogue/Rogue_AnimationSet.asset";
    private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    private const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindablePath = "Assets/Data/Input/RebindableActions.asset";
    private const float Intensity = 0.8f;
    private const int Release = 4;
    private const int HiddenRunFrame = 3;
    private static readonly string[] HandClips = { "CastBolt", "CastMend", "SwordSlash" };
    private static readonly string[] HipClips = { "GreatswordCleave", "BowShot" };

    private readonly List<Object> created = new();
    private ManualClock clock;
    private CharacterLocomotionSettings locomotion;
    private CharacterAnimationSet set;

    /// <summary>
    /// Builds locomotion settings and a set carrying a lantern on every frame, each from a point of its own: an idle, a
    /// run whose fourth frame the body hides the lantern on, a cast with its release on frame 4, glowing there, and the
    /// same cast drawn over the run's eight frames.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        locomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        set = Track(CharacterAnimationSet.Create(new[]
        {
            Clip("Idle", CharacterGait.Idle, new[] { 0.25f }, 0f, -1, 0),
            Clip("Run", CharacterGait.Run, Enumerable.Repeat(0.1f, 8).ToArray(), 4f, -1, 0),
            Clip("Cast", CharacterGait.None, CastSeconds(), 0f, Release, 0),
            Clip("CastRun", CharacterGait.None, CastSeconds(), 0f, Release, 8)
        }));
    }

    /// <summary>
    /// Destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made is Component component ? component.gameObject : made);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// The animator shows, with each sprite, its clip's lantern layer and light: running, standing in a cast, held
    /// through a hit-stop, and over the run's frame the legs show in a cast drawn over the gait; on a frame where the
    /// body hides the lantern it shows no layer but still the light.
    /// </summary>
    [Test]
    public void TheAnimator_ShowsEachFramesLantern_HeldWithItsSprite_AndOverTheGait()
    {
        CharacterAnimator animator = new(set, locomotion, clock);
        CharacterAnimationClip run = set.Find("Run");
        Vector2 running = Vector2.right * 4f;
        animator.Update(running, Vector2.right);
        bool sawHidden = false;
        for (int step = 0; step < 16; step++)
        {
            clock.Advance(0.05f);
            animator.Update(running, Vector2.right);
            Assert.That(animator.Lantern, Is.SameAs(run.LanternAt(CharacterDirections.East, animator.Frame, 0)), $"step {step}");
            Assert.That(animator.LanternLight, Is.EqualTo(run.LanternLightAt(CharacterDirections.East, animator.Frame, 0)), $"step {step}");
            Assert.That(animator.LanternLight.IsLit, Is.True, "Every frame's lantern shines, hidden or not.");
            sawHidden |= animator.Frame == HiddenRunFrame;
        }

        Assert.That(sawHidden, Is.True);
        Assert.That(run.LanternAt(CharacterDirections.East, HiddenRunFrame, 0), Is.Null, "The body hides the whole lantern on that frame.");

        CharacterAnimationClip cast = set.Find("Cast");
        AttackTimeline timeline = new(0.35f, 0.06f, 0.24f);
        animator.PlayCast(cast, null, Vector2.right, false, timeline, 0f, false);
        clock.Advance(0.36f);
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(Release));
        Assert.That(animator.Lantern, Is.SameAs(cast.LanternAt(CharacterDirections.East, Release, 0)));
        animator.Hold(0.08f);
        animator.Update(Vector2.zero, Vector2.right);
        clock.Advance(0.07f);
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Lantern, Is.SameAs(cast.LanternAt(CharacterDirections.East, Release, 0)), "The hold keeps the release's lantern.");
        Assert.That(animator.LanternLight, Is.EqualTo(cast.LanternLightAt(CharacterDirections.East, Release, 0)));

        CharacterAnimationClip castRun = set.Find("CastRun");
        clock.Advance(1f);
        animator.Update(Vector2.up * 4f, Vector2.up);
        animator.PlayCast(cast, castRun, Vector2.zero, true, timeline, 0f, false);
        for (int step = 0; step < 12; step++)
        {
            clock.Advance(0.02f);
            animator.Update(Vector2.up * 4f, Vector2.up);
            int gaitFrame = (int)(animator.Phase * 8f) % 8;
            Assert.That(animator.Lantern, Is.SameAs(castRun.LanternAt(CharacterDirections.North, animator.Frame, gaitFrame)), $"moving cast step {step}");
            Assert.That(animator.LanternLight, Is.EqualTo(castRun.LanternLightAt(CharacterDirections.North, animator.Frame, gaitFrame)), $"moving cast step {step}");
        }
    }

    /// <summary>
    /// The visuals draw the lantern's layer and cast its light, where the frame hangs it at the intensity it burns at,
    /// only while it burns: out, the layer shows nothing and the light is off; a frame hiding the lantern shows no layer
    /// but keeps its light; a cast's light shines beside it; and the light is off while the visuals are.
    /// </summary>
    [Test]
    public void TheVisuals_DrawTheLanternAndCastItsLight_OnlyWhileItBurns()
    {
        CharacterVisuals visuals = CreateVisuals(out SpriteRenderer layer, out Light2D light, out Light2D castLight);
        CharacterAnimationClip idle = set.Find("Idle");
        visuals.Tick();
        Assert.That(layer.sprite, Is.Null, "Out, no lantern shows.");
        Assert.That(light.enabled, Is.False);

        visuals.ShowLantern(true, Intensity);
        visuals.Tick();
        CharacterFrameLight hung = idle.LanternLightAt(CharacterDirections.South, 0, 0);
        Assert.That(layer.sprite, Is.SameAs(idle.LanternAt(CharacterDirections.South, 0, 0)));
        Assert.That(light.enabled, Is.True);
        Assert.That((Vector2)light.transform.localPosition, Is.EqualTo(hung.Offset));
        Assert.That(light.intensity, Is.EqualTo(Intensity).Within(1e-5f));

        CharacterAnimationClip run = set.Find("Run");
        visuals.SetLocomotion(Vector2.down * 4f, Vector2.down);
        bool sawHidden = false;
        for (int step = 0; step < 16; step++)
        {
            clock.Advance(0.05f);
            visuals.Tick();
            int frame = visuals.Animator.Frame;
            Assert.That(layer.sprite, Is.SameAs(run.LanternAt(CharacterDirections.South, frame, 0)), $"step {step}");
            Assert.That(light.enabled, Is.True, $"step {step}");
            Assert.That((Vector2)light.transform.localPosition, Is.EqualTo(run.LanternLightAt(CharacterDirections.South, frame, 0).Offset), $"step {step}");
            sawHidden |= frame == HiddenRunFrame && layer.sprite == null;
        }

        Assert.That(sawHidden, Is.True, "A frame hiding the lantern shows no layer, its light still on.");

        visuals.SetLocomotion(Vector2.zero, Vector2.down);
        visuals.PlayCast(set.Find("Cast"), null, Vector2.down, false, new AttackTimeline(0.35f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.36f);
        visuals.Tick();
        Assert.That(castLight.enabled, Is.True, "A cast's light shines beside the lantern's.");
        Assert.That(light.enabled, Is.True);

        visuals.ShowLantern(false, 0f);
        visuals.Tick();
        Assert.That(layer.sprite, Is.Null, "Put out, the lantern no longer shows.");
        Assert.That(light.enabled, Is.False);

        visuals.ShowLantern(true, Intensity);
        visuals.Tick();
        typeof(CharacterVisuals).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(visuals, null);
        Assert.That(light.enabled, Is.False, "The light is off while the visuals are.");
    }

    /// <summary>
    /// Once warm, a burning lantern shown over a run, its layer changing, its light moving and flickering, allocates
    /// nothing. Turning a Light 2D on or off allocates in the editor only, so the lantern burns through the stretch.
    /// </summary>
    [Test]
    public void ShowingTheLantern_AllocatesNothingWhileItBurns()
    {
        CharacterVisuals visuals = CreateVisuals(out _, out Light2D light, out _);
        PlayerLantern lantern = CreateLantern(visuals);
        visuals.SetLocomotion(Vector2.right * 4f, Vector2.right);

        void Run()
        {
            for (int step = 0; step < 24; step++)
            {
                clock.Advance(0.05f);
                lantern.Show(true);
                visuals.Tick();
            }
        }

        lantern.Show(true);
        visuals.Tick();
        Run();
        Run();
        Assert.That(() => Run(), Is.Not.AllocatingGCMemory());
        Assert.That(light.enabled, Is.True);
    }

    /// <summary>
    /// The owner's lantern lights itself as the night lights' share reaches its share at sunset and goes out as it
    /// falls below at sunrise, each only as it crosses, raising the change each time; the key lights it or puts it out
    /// at any time, and that choice holds until the share next crosses.
    /// </summary>
    [Test]
    public void TheOwnersLantern_LightsItselfAtSunset_GoesOutAtSunrise_AndTheKeyHoldsUntilTheNext()
    {
        CharacterVisuals visuals = CreateVisuals(out _, out _, out _);
        PlayerLantern lantern = CreateLantern(visuals);
        List<bool> changes = new();
        lantern.Changed += changes.Add;
        lantern.DecideLocally();
        Assert.That(lantern.Deciding, Is.True);
        Assert.That(lantern.Lit, Is.False, "Without night lights it starts out.");

        lantern.Decide(0.5f);
        Assert.That(lantern.Lit, Is.False, "Evening: not yet.");
        lantern.Decide(lantern.AutoShare);
        Assert.That(lantern.Lit, Is.True, "Sunset lights it.");
        Assert.That(visuals.LanternLit, Is.True);
        lantern.Decide(1f);
        Assert.That(changes, Is.EqualTo(new[] { true }));

        lantern.Toggle();
        Assert.That(lantern.Lit, Is.False, "The key puts it out at night.");
        lantern.Decide(1f);
        Assert.That(lantern.Lit, Is.False, "and the night does not light it again.");
        lantern.Decide(0f);
        Assert.That(lantern.Lit, Is.False, "Sunrise.");
        lantern.Toggle();
        Assert.That(lantern.Lit, Is.True, "The key lights it by day.");
        lantern.Decide(0.2f);
        Assert.That(lantern.Lit, Is.True, "and the day does not put it out.");
        lantern.Decide(0.9f);
        Assert.That(lantern.Lit, Is.True, "Sunset keeps it lit.");
        lantern.Decide(0.1f);
        Assert.That(lantern.Lit, Is.False, "Sunrise puts it out.");
        Assert.That(changes, Is.EqualTo(new[] { true, false, true, false }));
    }

    /// <summary>
    /// A copy's lantern burns or goes out as its owner decides, raising no change of its own, and the key does nothing
    /// to it.
    /// </summary>
    [Test]
    public void ACopysLantern_ShowsItsOwnersChoice_AndTheKeyDoesNothing()
    {
        CharacterVisuals visuals = CreateVisuals(out _, out _, out _);
        PlayerLantern lantern = CreateLantern(visuals);
        int changes = 0;
        lantern.Changed += _ => changes++;

        lantern.Toggle();
        Assert.That(lantern.Lit, Is.False, "Only the owner's key counts.");
        lantern.Show(true);
        Assert.That(lantern.Lit, Is.True);
        Assert.That(visuals.LanternLit, Is.True);
        lantern.Show(false);
        Assert.That(visuals.LanternLit, Is.False);
        Assert.That(changes, Is.Zero);
    }

    /// <summary>
    /// The lantern's light flickers at each of the night lights' steps to a strength within its share either way, the
    /// same step always giving the same strength.
    /// </summary>
    [Test]
    public void TheLanternsLight_FlickersWithinItsShare_StepByStep()
    {
        PlayerLantern lantern = CreateLantern(CreateVisuals(out _, out _, out _));
        HashSet<float> strengths = new();
        for (int step = 0; step < 200; step++)
        {
            float strength = lantern.StrengthAt(step);
            Assert.That(strength, Is.InRange(Intensity * 0.96f, Intensity * 1.04f), $"step {step}");
            Assert.That(lantern.StrengthAt(step), Is.EqualTo(strength));
            strengths.Add(strength);
        }

        Assert.That(strengths.Count, Is.GreaterThan(100), "It flickers.");
    }

    /// <summary>
    /// The rogue's set carries the lantern on every clip, a light on every frame at full as the baker draws it, and a
    /// layer on every frame but those where the body hides the whole lantern.
    /// </summary>
    [Test]
    public void TheRoguesSet_CarriesTheLanternOnEveryFrame_AsTheBakerDrawsIt()
    {
        CharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterAnimationSet rogue = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(RogueSetPath);
        CharacterBaker baker = new(recipe);
        foreach (CharacterAnimationClip shipped in rogue.Clips)
        {
            Assert.That(shipped.HasLantern, Is.True, shipped.Name);
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int gaitFrame = 0; gaitFrame < Mathf.Max(1, shipped.GaitFrames); gaitFrame++)
                {
                    for (int frame = 0; frame < shipped.FrameCount; frame++)
                    {
                        Assert.That(shipped.LanternLightAt(direction, frame, gaitFrame).Strength, Is.EqualTo(1f), $"{shipped.Name} {direction} {frame}");
                    }
                }
            }
        }

        foreach (string name in HandClips.Concat(HipClips).Append("Run"))
        {
            CharacterClip clip = recipe.Clips.First(c => c.Name == name);
            CharacterAnimationClip shipped = rogue.Find(name);
            CharacterSheet sheet = baker.Bake(clip);
            CharacterFrameLight[] baked = CharacterSheetWriter.LanternLights(sheet);
            int columns = sheet.FrameCount;
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int column = 0; column < columns; column++)
                {
                    CharacterFrameLight expected = baked[direction * columns + column];
                    CharacterFrameLight actual = shipped.LanternLightAt(direction, column, 0);
                    Assert.That(Vector2.Distance(actual.Offset, expected.Offset), Is.LessThan(1e-5f), $"{name} {direction} {column}");
                    bool drawn = LayerPixels(sheet, direction, column) > 0;
                    Assert.That(shipped.LanternAt(direction, column, 0) != null, Is.EqualTo(drawn), $"{name} {direction} {column}: a layer exactly where the lantern shows.");
                }
            }
        }
    }

    /// <summary>
    /// The lantern hangs plumb under the left hand in every frame of the clips that leave that hand free, its light at
    /// the glass's middle, whether or not the body hides it; while a weapon takes the hand it hangs at the left hip
    /// instead, by the hook there, away from the hand.
    /// </summary>
    [Test]
    public void TheLantern_HangsFromTheLeftHand_OrAtTheHipWhileAWeaponTakesIt()
    {
        CharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterLanternArt lantern = recipe.Lantern;
        CharacterBaker baker = new(recipe);
        CharacterCanvas view = new(8, 8, Vector2Int.zero, baker.Scale);
        float drop = recipe.HandRadius * lantern.HandDrop + (lantern.BailDrop + lantern.RoofHeight + lantern.GlassHeight * 0.5f) * lantern.Size;
        foreach (string name in HandClips.Concat(HipClips))
        {
            CharacterClip clip = recipe.Clips.First(c => c.Name == name);
            CharacterSheet sheet = baker.Bake(clip);
            bool atHip = HipClips.Contains(name);
            int awayFromHand = 0;
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int frame = 0; frame < sheet.FrameCount; frame++)
                {
                    CharacterPose pose = clip.Motion.PoseAt(frame, recipe, clip.Weapon);
                    CharacterJoints joints = CharacterRig.Solve(recipe, pose, clip.Weapon);
                    float facing = CharacterDirections.Angle(direction);
                    Vector3 underHand = view.Project(World(joints.LeftHand, facing) + Vector3.forward * -drop);
                    Vector3 hook = view.Project(World(joints.Pelvis + joints.Spine * lantern.HipHook.z + joints.HipAxis * lantern.HipHook.x + joints.HipForward * lantern.HipHook.y, facing));
                    Vector2 centre = sheet.LanternCentres[direction * sheet.FrameCount + frame];
                    string at = $"{name} {CharacterDirections.Name(direction)} {frame}";
                    if (!atHip)
                    {
                        Assert.That(Vector2.Distance(centre, underHand), Is.LessThan(0.01f), $"{at} hangs plumb under the left hand.");
                        continue;
                    }

                    Assert.That(centre.y, Is.LessThan(hook.y), $"{at} hangs below the hip's hook.");
                    Assert.That(Vector2.Distance(centre, hook), Is.LessThan(12f), $"{at} hangs at the hip.");
                    awayFromHand += Vector2.Distance(centre, underHand) > 3f ? 1 : 0;
                }
            }

            if (atHip)
            {
                Assert.That(awayFromHand, Is.GreaterThan(sheet.FrameCount * CharacterDirections.Count * 3 / 4), $"{name}: the weapon holds the hand away from the lantern.");
            }
        }
    }

    /// <summary>
    /// Each frame's lantern layer, drawn over its sheet frame, gives exactly the frame drawn with the lantern, the sheet
    /// frame staying the one drawn without it; the layer holds only the pixels the lantern changed, and it glows at the
    /// lantern's glass and wick and nowhere else.
    /// </summary>
    [Test]
    public void TheLanternLayer_OverTheSheet_IsTheFrameDrawnWithTheLantern_GlowingAtItsGlass()
    {
        CharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterBaker baker = new(recipe);
        foreach (string name in new[] { "Walk", "BowShot" })
        {
            CharacterClip clip = recipe.Clips.First(c => c.Name == name);
            CharacterSheet sheet = baker.Bake(clip);
            Vector2Int size = clip.FrameSize;
            int wrong = 0;
            int glowing = 0;
            int layer = 0;
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                foreach (int frame in new[] { 0, 3 })
                {
                    CharacterCanvas canvas = baker.Render(clip, direction, frame);
                    Color32[] plain = baker.Shade(canvas, clip.Weapon, clip.Motion.Light);
                    baker.DrawLantern(clip, direction, frame, canvas);
                    Color32[] carried = baker.Shade(canvas, clip.Weapon, clip.Motion.Light);
                    RectInt rect = CharacterSheet.FrameRect(direction, frame, size);
                    for (int y = 0; y < size.y; y++)
                    {
                        for (int x = 0; x < size.x; x++)
                        {
                            int i = y * size.x + x;
                            int o = (rect.y + y) * sheet.Width + rect.x + x;
                            Color32 over = sheet.LanternPixels[o];
                            Color32 composite = over.a > 0 ? over : sheet.Pixels[o];
                            bool glows = canvas.IsFilled(x, y) && CharacterMaterials.Glows(canvas.MaterialAt(x, y)) && over.a > 0;
                            wrong += !sheet.Pixels[o].Equals(plain[i]) || !composite.Equals(carried[i]) || (over.a > 0) == carried[i].Equals(plain[i]) ? 1 : 0;
                            wrong += sheet.LanternGlow[o].a != (glows ? 255 : 0) ? 1 : 0;
                            glowing += glows ? 1 : 0;
                            layer += over.a > 0 ? 1 : 0;
                        }
                    }
                }
            }

            Assert.That(wrong, Is.Zero, name);
            Assert.That(layer, Is.GreaterThan(0), name);
            Assert.That(glowing, Is.GreaterThan(0).And.LessThan(layer), $"{name}: the glass glows, the iron does not.");
        }
    }

    /// <summary>
    /// The lantern's layer packs each frame cut to the box around its pixels into a cell of the clip's largest box, a
    /// slice per frame where the lantern shows, pivoted on the feet: unpacked by its pivot, every slice gives back its
    /// frame's layer exactly, and the packed sheet is a small share of the clip's.
    /// </summary>
    [Test]
    public void PackLantern_CutsEachFrameToItsBox_PivotedOnTheFeet()
    {
        CharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterClip clip = recipe.Clips.First(c => c.Name == "Run");
        CharacterSheet sheet = new CharacterBaker(recipe).Bake(clip);
        IReadOnlyList<SpriteSheetFiles.Slice> slices = CharacterSheetWriter.PackLantern("Rogue", sheet, out Vector2Int size, out Color32[] pixels, out Color32[] glow);
        Dictionary<string, SpriteSheetFiles.Slice> byName = slices.ToDictionary(slice => slice.Name);
        Assert.That(size.x * size.y * 4, Is.LessThan(sheet.Width * sheet.Height), "The packed layer is a small share of the sheet.");
        Color32[] unpacked = new Color32[sheet.LanternPixels.Length];
        Color32[] unpackedGlow = new Color32[unpacked.Length];
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int column = 0; column < sheet.FrameCount; column++)
            {
                string name = CharacterSheetWriter.SliceName("Rogue", "RunLantern", direction, column);
                bool shows = LayerPixels(sheet, direction, column) > 0;
                Assert.That(byName.ContainsKey(name), Is.EqualTo(shows), name);
                if (!shows)
                {
                    continue;
                }

                SpriteSheetFiles.Slice slice = byName[name];
                RectInt frame = CharacterSheet.FrameRect(direction, column, clip.FrameSize);
                Vector2Int feetInSlice = Vector2Int.RoundToInt(new Vector2(slice.Pivot.x * slice.Rect.width, slice.Pivot.y * slice.Rect.height));
                Vector2Int origin = clip.Pivot - feetInSlice;
                for (int y = 0; y < (int)slice.Rect.height; y++)
                {
                    for (int x = 0; x < (int)slice.Rect.width; x++)
                    {
                        int from = ((int)slice.Rect.y + y) * size.x + (int)slice.Rect.x + x;
                        int to = (frame.y + origin.y + y) * sheet.Width + frame.x + origin.x + x;
                        unpacked[to] = pixels[from];
                        unpackedGlow[to] = glow[from];
                    }
                }
            }
        }

        Assert.That(unpacked, Is.EqualTo(sheet.LanternPixels), "Unpacked by their pivots, the slices give back the layer.");
        Assert.That(unpackedGlow, Is.EqualTo(sheet.LanternGlow));
    }

    /// <summary>
    /// The written lantern sheets are the packed layers, sliced as packed, with their packed glow as their secondary
    /// texture <c>_GlowTex</c>, and the rogue's set draws each frame's layer from them.
    /// </summary>
    [Test]
    public void TheWrittenLanternSheets_AreThePackedLayers_WithTheirGlow()
    {
        CharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterAnimationSet rogue = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(RogueSetPath);
        CharacterBaker baker = new(recipe);
        foreach (string name in new[] { "Idle", "Run", "GreatswordCleave" })
        {
            CharacterClip clip = recipe.Clips.First(c => c.Name == name);
            CharacterSheet sheet = baker.Bake(clip);
            IReadOnlyList<SpriteSheetFiles.Slice> slices = CharacterSheetWriter.PackLantern(recipe.SheetName, sheet, out Vector2Int size, out Color32[] pixels, out Color32[] glow);
            string path = CharacterSheetWriter.SheetPath(recipe.SheetName, name + CharacterSheetWriter.LanternSuffix);
            string glowPath = CharacterSheetWriter.GlowPath(recipe.SheetName, name + CharacterSheetWriter.LanternSuffix);
            Assert.That(ReadPng(path), Is.EqualTo(pixels), name);
            Assert.That(ReadPng(glowPath), Is.EqualTo(glow), name);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.secondarySpriteTextures.Length, Is.EqualTo(1), name);
            Assert.That(importer.secondarySpriteTextures[0].name, Is.EqualTo(SpriteSheetFiles.GlowTextureName));
            Assert.That(importer.secondarySpriteTextures[0].texture, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(glowPath)));

            CharacterAnimationClip shipped = rogue.Find(name);
            Dictionary<string, SpriteSheetFiles.Slice> byName = slices.ToDictionary(slice => slice.Name);
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int column = 0; column < sheet.FrameCount; column++)
                {
                    Sprite sprite = shipped.LanternAt(direction, column, 0);
                    if (sprite == null)
                    {
                        continue;
                    }

                    SpriteSheetFiles.Slice slice = byName[sprite.name];
                    Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(path));
                    Assert.That(sprite.rect, Is.EqualTo(slice.Rect), sprite.name);
                    Assert.That(Vector2.Distance(sprite.pivot, new Vector2(slice.Pivot.x * slice.Rect.width, slice.Pivot.y * slice.Rect.height)), Is.LessThan(1e-3f), sprite.name);
                    Assert.That(sprite.name, Is.EqualTo(CharacterSheetWriter.SliceName(recipe.SheetName, name + CharacterSheetWriter.LanternSuffix, direction, column)));
                }
            }
        }
    }

    /// <summary>
    /// A hit's flash mixes the lantern's layer toward the flash colour with the body, through a property block of its
    /// own, and both draw as their material does again once it ends.
    /// </summary>
    [Test]
    public void AHitsFlash_CoversTheLanternWithTheBody()
    {
        CharacterVisuals visuals = CreateVisuals(out SpriteRenderer layer, out _, out _);
        SpriteRenderer body = visuals.GetComponent<SpriteRenderer>();
        HitFeedback feedback = Track(new GameObject("Unit")).AddComponent<HitFeedback>();
        feedback.Configure(body, visuals.transform, visuals, null, 0.7f);
        SerializedObject serialized = new(feedback);
        serialized.FindProperty("lantern").objectReferenceValue = layer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        MaterialPropertyBlock block = new();

        feedback.Flash(Color.white, 1f, 0.05f);
        layer.GetPropertyBlock(block);
        Assert.That(block.GetFloat("_FlashAmount"), Is.EqualTo(1f), "The lantern flashes with the body.");
        body.GetPropertyBlock(block);
        Assert.That(block.GetFloat("_FlashAmount"), Is.EqualTo(1f));

        feedback.Tick(0.02f);
        feedback.Tick(0.06f);
        Assert.That(layer.HasPropertyBlock(), Is.False, "and draws as its material does once the flash ends.");
        Assert.That(body.HasPropertyBlock(), Is.False);
    }

    /// <summary>
    /// The player prefab carries the lantern: its layer drawn by a renderer of its own under the visuals, over the body
    /// inside the character's sorting group at order 1, with the body's material and rendering layers and no sprite
    /// until it burns, flashing with it; its light a point light in the lamp glass's tone (255, 204, 110) of radius 3.5
    /// with URP's default falloff, on blend style 0 and the Default, Terrain and Entities sorting layers, casting no
    /// shadow, off; and the lantern burning at 0.8, flickering by 0.04, lighting itself at the night lights' share at
    /// sunset.
    /// </summary>
    [Test]
    public void ThePlayerPrefab_CarriesTheLantern()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterVisuals visuals = player.GetComponentInChildren<CharacterVisuals>(true);
        SpriteRenderer body = visuals.GetComponent<SpriteRenderer>();
        SpriteRenderer layer = visuals.LanternRenderer;
        Assert.That(layer, Is.Not.Null);
        Assert.That(layer.transform.parent, Is.EqualTo(visuals.transform));
        Assert.That(layer.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(layer.sprite, Is.Null);
        Assert.That(layer.sortingLayerName, Is.EqualTo(body.sortingLayerName));
        Assert.That(layer.sortingOrder, Is.EqualTo(body.sortingOrder + 1), "Over the body inside the character's group.");
        Assert.That(layer.sharedMaterial, Is.EqualTo(body.sharedMaterial));
        Assert.That(layer.renderingLayerMask, Is.EqualTo(body.renderingLayerMask), "It casts the sun's shadow with the body.");
        Assert.That(player.GetComponent<HitFeedback>().Lantern, Is.EqualTo(layer), "It flashes with the body.");

        Light2D light = visuals.LanternLight;
        Assert.That(light, Is.Not.Null);
        Assert.That(light, Is.Not.EqualTo(visuals.CastLight));
        Assert.That(light.transform.parent, Is.EqualTo(visuals.transform));
        Assert.That(light.lightType, Is.EqualTo(Light2D.LightType.Point));
        Assert.That(light.blendStyleIndex, Is.Zero);
        Assert.That((Color32)light.color, Is.EqualTo(new Color32(255, 204, 110, 255)));
        Assert.That(light.pointLightOuterRadius, Is.EqualTo(3.5f));
        Assert.That(light.pointLightInnerRadius, Is.Zero);
        Assert.That(light.falloffIntensity, Is.EqualTo(0.5f));
        Assert.That(light.shadowsEnabled, Is.False, "It casts no shadow.");
        Assert.That(light.enabled, Is.False, "Off until it burns.");
        string[] layers = light.targetSortingLayers.Select(SortingLayer.IDToName).ToArray();
        Assert.That(layers, Is.EquivalentTo(new[] { "Default", "Terrain", "Entities" }));

        PlayerLantern lantern = player.GetComponent<PlayerLantern>();
        Assert.That(lantern, Is.Not.Null);
        Assert.That(lantern.Intensity, Is.EqualTo(Intensity));
        Assert.That(lantern.Flicker, Is.EqualTo(0.04f));
        Assert.That(lantern.AutoShare, Is.EqualTo(0.7f));
        SerializedObject serialized = new(lantern);
        Assert.That(serialized.FindProperty("visuals").objectReferenceValue, Is.EqualTo(visuals));
    }

    /// <summary>
    /// The lantern key is the project's player action Lantern on L, rebindable as Lantern under Other; read by the
    /// local command source, it reaches the local player's lantern through its controller, which lights it or puts it
    /// out; a remote copy's controller reads no command.
    /// </summary>
    [Test]
    public void TheLanternKey_IsLRebindableUnderOther_AndReachesTheLocalPlayersLantern()
    {
        InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        InputAction action = actions.FindAction($"{PlayerInputService.PlayerMapName}/{PlayerInputService.LanternActionName}", true);
        Assert.That(action.bindings.Select(binding => binding.path), Is.EqualTo(new[] { "<Keyboard>/l" }));
        Assert.That(action.bindings[0].groups, Is.EqualTo("Keyboard&Mouse"));
        RebindableActionList list = AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindablePath);
        int index = Enumerable.Range(0, list.Count).Single(i => list[i].Action.action.name == PlayerInputService.LanternActionName);
        Assert.That(list[index].Label, Is.EqualTo("Lantern"));
        Assert.That(list[index].Section, Is.EqualTo("Other"));

        GameObject cameraObject = Track(new GameObject("Camera"));
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        FakePlayerInput input = new() { GameplayEnabled = true, LanternPressedThisFrame = true };
        LocalPlayerCommandSource source = new(input, camera, null);
        Assert.That(source.ReadCommand().LanternPressed, Is.True);
        input.LanternPressedThisFrame = false;
        Assert.That(source.ReadCommand().LanternPressed, Is.False);

        GameObject root = Track(new GameObject("Player"));
        root.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.2f);
        root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        PlayerController controller = root.AddComponent<PlayerController>();
        controller.Construct(clock);
        TestPlayerControlSettings.Assign(controller, Track(TestPlayerControlSettings.Create()));
        PlayerLantern lantern = root.AddComponent<PlayerLantern>();
        lantern.Configure(CreateVisuals(out _, out _, out _), Intensity, 0.04f);
        lantern.DecideLocally();
        controller.SetCommandSource(source);

        input.LanternPressedThisFrame = true;
        controller.Tick();
        Assert.That(lantern.Lit, Is.True, "The key lights it.");
        input.LanternPressedThisFrame = false;
        controller.Tick();
        Assert.That(lantern.Lit, Is.True, "Only a press toggles it.");
        input.LanternPressedThisFrame = true;
        controller.Tick();
        Assert.That(lantern.Lit, Is.False, "and puts it out.");

        controller.SetSimulatesMovement(false);
        controller.Tick();
        Assert.That(lantern.Lit, Is.False, "A remote copy reads no command.");
    }

    /// <summary>
    /// The character's space point <paramref name="local"/> in the world, facing <paramref name="facingDegrees"/>, as
    /// the painter maps it.
    /// </summary>
    private static Vector3 World(Vector3 local, float facingDegrees)
    {
        float facing = facingDegrees * Mathf.Deg2Rad;
        Vector3 forward = new(Mathf.Cos(facing), Mathf.Sin(facing), 0f);
        Vector3 right = new(Mathf.Sin(facing), -Mathf.Cos(facing), 0f);
        return local.x * right + local.y * forward + local.z * Vector3.forward;
    }

    /// <summary>
    /// How many pixels of frame <paramref name="column"/> facing <paramref name="direction"/> the lantern's layer of
    /// <paramref name="sheet"/> holds.
    /// </summary>
    private static int LayerPixels(CharacterSheet sheet, int direction, int column)
    {
        RectInt rect = CharacterSheet.FrameRect(direction, column, sheet.Clip.FrameSize);
        int count = 0;
        for (int y = rect.yMin; y < rect.yMax; y++)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                count += sheet.LanternPixels[y * sheet.Width + x].a > 0 ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>
    /// The pixels of the PNG at <paramref name="path"/>, rows from the bottom.
    /// </summary>
    private Color32[] ReadPng(string path)
    {
        Texture2D texture = Track(new Texture2D(2, 2));
        texture.LoadImage(File.ReadAllBytes(path));
        return texture.GetPixels32();
    }

    /// <summary>
    /// Character visuals of the test set on the manual clock, with a cast light, a lantern layer renderer and a lantern
    /// light on children.
    /// </summary>
    private CharacterVisuals CreateVisuals(out SpriteRenderer layer, out Light2D light, out Light2D castLight)
    {
        GameObject body = Track(new GameObject("Visuals"));
        body.AddComponent<SpriteRenderer>();
        CharacterVisuals visuals = body.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = locomotion;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);
        castLight = Child<Light2D>(body, "CastLight");
        castLight.lightType = Light2D.LightType.Point;
        castLight.enabled = false;
        visuals.Configure(castLight, 1.2f);
        layer = Child<SpriteRenderer>(body, "Lantern");
        light = Child<Light2D>(body, "LanternLight");
        light.lightType = Light2D.LightType.Point;
        light.enabled = false;
        visuals.ConfigureLantern(layer, light);
        return visuals;
    }

    /// <summary>
    /// A lantern of <see cref="Intensity"/> flickering by 0.04 on its own object, shown by <paramref name="visuals"/>.
    /// </summary>
    private PlayerLantern CreateLantern(CharacterVisuals visuals)
    {
        PlayerLantern lantern = Track(new GameObject("Lantern")).AddComponent<PlayerLantern>();
        lantern.Configure(visuals, Intensity, 0.04f);
        return lantern;
    }

    /// <summary>
    /// A new component of <typeparamref name="T"/> on a child named <paramref name="name"/> of <paramref name="parent"/>.
    /// </summary>
    private static T Child<T>(GameObject parent, string name) where T : Component
    {
        GameObject child = new(name);
        child.transform.SetParent(parent.transform, false);
        return child.AddComponent<T>();
    }

    /// <summary>
    /// A clip of <paramref name="frameSeconds"/> drawn over <paramref name="gaitFrames"/> gait frames (0 for standing),
    /// each sprite with a lantern layer and light of its own, the run's fourth frame hiding its layer; a clip with a
    /// contact frame glows at full there, in frost blue.
    /// </summary>
    private CharacterAnimationClip Clip(string name, CharacterGait gait, float[] frameSeconds, float naturalSpeed, int contact, int gaitFrames)
    {
        int count = CharacterDirections.Count * frameSeconds.Length * Mathf.Max(1, gaitFrames);
        Sprite[] sprites = Sprites(name, count);
        Sprite[] layers = Sprites(name + "Lantern", count);
        CharacterFrameLight[] lanternLights = new CharacterFrameLight[count];
        CharacterFrameLight[] castLights = contact >= 0 ? new CharacterFrameLight[count] : null;
        for (int i = 0; i < count; i++)
        {
            lanternLights[i] = new CharacterFrameLight(new Vector2(-0.2f + 0.001f * i, 0.25f + 0.002f * i), 1f);
            if (name == "Run" && i % frameSeconds.Length == HiddenRunFrame)
            {
                layers[i] = null;
            }

            if (castLights != null)
            {
                castLights[i] = new CharacterFrameLight(new Vector2(0.3f, 0.6f), i % frameSeconds.Length == contact ? 1f : 0f);
            }
        }

        Color frost = castLights != null ? new Color(0.35f, 0.7f, 0.95f, 1f) : default;
        return CharacterAnimationClip.Create(name, gait, false, sprites, frameSeconds, naturalSpeed, contact, gaitFrames, frost, castLights, layers, lanternLights);
    }

    /// <summary>
    /// The cast's frame durations: a gather of 0.09, 0.09, 0.09 and 0.08 s, a release of 0.09 s and a follow-through of
    /// 0.09, 0.1 and 0.1 s.
    /// </summary>
    private static float[] CastSeconds()
    {
        return new[] { 0.09f, 0.09f, 0.09f, 0.08f, 0.09f, 0.09f, 0.1f, 0.1f };
    }

    /// <summary>
    /// <paramref name="count"/> sprites of their own named after <paramref name="name"/>.
    /// </summary>
    private Sprite[] Sprites(string name, int count)
    {
        Texture2D texture = Track(new Texture2D(4, 4));
        Sprite[] sprites = new Sprite[count];
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = Track(Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), Vector2.zero, 32f));
            sprites[i].name = $"{name}_{i}";
        }

        return sprites;
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
