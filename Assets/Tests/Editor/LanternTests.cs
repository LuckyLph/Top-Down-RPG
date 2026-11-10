using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for a carried lantern (Docs/Lighting.md, Phase 9), which no character the game ships carries since the
/// player's was removed: the animator showing each frame's lantern layer and light, held with its sprite and over a
/// gait; the visuals drawing the layer and casting its light only while it burns, beside a cast's light; showing it
/// allocating nothing; the baker hanging it plumb from the left hand or, while a weapon takes that hand, at the hip;
/// the layer over a sheet being the frame drawn with the lantern, glowing at its glass; the layer packed tightly with
/// its slices pivoted on the feet; and a hit's flash covering it with the body.
/// </summary>
public class LanternTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const string LanternPath = "Assets/Settings/Characters/Lantern.asset";
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
        visuals.SetLocomotion(Vector2.right * 4f, Vector2.right);

        void Run()
        {
            for (int step = 0; step < 24; step++)
            {
                clock.Advance(0.05f);
                visuals.ShowLantern(true, Intensity * (1f + 0.04f * (step % 3 - 1)));
                visuals.Tick();
            }
        }

        visuals.ShowLantern(true, Intensity);
        visuals.Tick();
        Run();
        Run();
        Assert.That(() => Run(), Is.Not.AllocatingGCMemory());
        Assert.That(light.enabled, Is.True);
    }

    /// <summary>
    /// The lantern hangs plumb under the left hand in every frame of the clips that leave that hand free, its light at
    /// the glass's middle, whether or not the body hides it; while a weapon takes the hand it hangs at the left hip
    /// instead, by the hook there, away from the hand.
    /// </summary>
    [Test]
    public void TheLantern_HangsFromTheLeftHand_OrAtTheHipWhileAWeaponTakesIt()
    {
        CharacterRecipe recipe = LanternRecipe();
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
        CharacterRecipe recipe = LanternRecipe();
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
        CharacterRecipe recipe = LanternRecipe();
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
    /// The rogue's recipe carrying the lantern, made in memory, since no character the game ships carries one.
    /// </summary>
    private CharacterRecipe LanternRecipe()
    {
        CharacterRecipe recipe = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath)));
        SerializedObject serialized = new(recipe);
        serialized.FindProperty("lantern").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CharacterLanternArt>(LanternPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return recipe;
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
