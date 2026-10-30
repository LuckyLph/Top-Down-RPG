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
/// Tests for spell lights (Docs/Lighting.md, Phase 2): a pooled effect's light shining in its variant's colour,
/// fading with its effect and off once back in the pool, and staying off for a variant without one; a cast's light
/// following the glow of each frame shown (where it shines from, its strength, its colour), held with its sprite,
/// over the gait for a cast drawn over one, and off between casts and while the visuals are off; both allocating
/// nothing once warm; the rogue's casts carrying the lights the baker draws, in their motions' rim tones; the
/// impacts lighting only the elemental types, in their middle colours; and the shipped lights set up as the light
/// textures read them.
/// </summary>
public class SpellLightTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const string RogueSetPath = "Assets/Sprites/Characters/Rogue/Rogue_AnimationSet.asset";
    private const int Release = 4;
    private const float Intensity = 1.2f;
    private static readonly float[] GlowStrengths = { 0f, 0.5f, 0.75f, 1f, 1f, 0.5f, 0.25f, 0f };
    private static readonly Color Frost = new(0.35f, 0.7f, 0.95f, 1f);

    private readonly List<Object> created = new();
    private ManualClock clock;
    private CharacterLocomotionSettings locomotion;
    private CharacterAnimationSet set;

    /// <summary>
    /// Builds locomotion settings and a set of an idle, a run, a cast whose frames glow as the rogue's do (none, a
    /// growing gather, the release, a ring and sparks fading, none) from a point of their own, and the same cast drawn
    /// over the run's eight frames.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        locomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        set = Track(CharacterAnimationSet.Create(new[]
        {
            CharacterAnimationClip.Create("Idle", CharacterGait.Idle, false, Sprites("Idle", 1), new[] { 0.25f }, 0f),
            CharacterAnimationClip.Create("Run", CharacterGait.Run, false, Sprites("Run", 8), Enumerable.Repeat(0.1f, 8).ToArray(), 4f),
            CastClip("Cast", 0),
            CastClip("CastRun", 8)
        }));
    }

    /// <summary>
    /// Destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (EffectPlayer effect in Object.FindObjectsByType<EffectPlayer>(FindObjectsInactive.Include))
        {
            if (effect != null && effect.SourcePrefab != null)
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }

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
    /// A pooled effect playing a variant with a light colour turns its light on at its intensity in that colour, fades
    /// it frame by frame over the effect's life, and turns it off as it goes back to the pool; a variant without a
    /// light colour leaves it off; the same instance played again lights again from full.
    /// </summary>
    [Test]
    public void APooledEffectsLight_ShinesAndFadesWithItsEffect_AndIsOffInThePool()
    {
        EffectPlayer prefab = CreateEffectPrefab(out _);
        EffectPool pool = new(default);
        EffectPlayer effect = pool.Play(prefab, 1, 0, Vector2.zero, 0f);
        Light2D light = effect.EffectLight;
        Assert.That(light, Is.Not.Null);
        Assert.That(light.enabled, Is.True);
        Assert.That(light.color, Is.EqualTo(Frost));
        Assert.That(light.intensity, Is.EqualTo(Intensity).Within(1e-5f));

        float last = light.intensity;
        for (int step = 1; step < 4; step++)
        {
            effect.Tick(0.04f);
            Assert.That(effect.IsPlaying, Is.True);
            Assert.That(light.enabled, Is.True);
            Assert.That(light.intensity, Is.EqualTo(Intensity * (1f - step / 4f)).Within(1e-4f), $"step {step}");
            Assert.That(light.intensity, Is.LessThan(last));
            last = light.intensity;
        }

        effect.Tick(0.04f);
        Assert.That(effect.IsPlaying, Is.False, "The effect ended and went back to its pool.");
        Assert.That(light.enabled, Is.False, "Its light went off with it.");

        Assert.That(pool.Play(prefab, 0, 0, Vector2.zero, 0f), Is.SameAs(effect));
        Assert.That(light.enabled, Is.False, "A variant without a light colour casts none.");
        effect.Tick(0.2f);

        Assert.That(pool.Play(prefab, 1, 0, Vector2.zero, 0f), Is.SameAs(effect));
        Assert.That(light.enabled, Is.True);
        Assert.That(light.intensity, Is.EqualTo(Intensity).Within(1e-5f));
        Assert.That(pool.CreatedCount, Is.EqualTo(1));
    }

    /// <summary>
    /// While a cast plays, the cast light shows the light of the frame each update shows: from where its glow shines,
    /// at its strength times the light's intensity, in the clip's colour, and off on frames without one; a hold keeps
    /// the held frame's light; it is off before and after the cast, and while the visuals are off.
    /// </summary>
    [Test]
    public void TheCastLight_FollowsTheGlowOfEachFrame_AndHoldsWithItsSprite()
    {
        CharacterVisuals visuals = CreateVisuals(out Light2D light);
        CharacterAnimationClip cast = set.Find("Cast");
        visuals.Tick();
        Assert.That(light.enabled, Is.False, "Standing casts no light.");

        visuals.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.35f, 0.06f, 0.24f), 0f, false);
        HashSet<int> lit = new();
        for (int step = 0; step < 34; step++)
        {
            visuals.Tick();
            CharacterAnimator animator = visuals.Animator;
            if (!animator.IsActing)
            {
                break;
            }

            CharacterFrameLight expected = cast.LightAt(CharacterDirections.East, animator.Frame, 0);
            Assert.That(light.enabled, Is.EqualTo(expected.IsLit), $"step {step}, frame {animator.Frame}");
            if (expected.IsLit)
            {
                lit.Add(animator.Frame);
                Assert.That((Vector2)light.transform.localPosition, Is.EqualTo(expected.Offset), $"step {step}");
                Assert.That(light.intensity, Is.EqualTo(Intensity * expected.Strength).Within(1e-5f), $"step {step}");
                Assert.That(light.color, Is.EqualTo(Frost));
            }

            clock.Advance(0.02f);
        }

        Assert.That(lit, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5, 6 }), "Every glowing frame lit.");
        Assert.That(visuals.Animator.IsActing, Is.False);
        Assert.That(light.enabled, Is.False, "The light went off with the cast.");

        visuals.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.35f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.36f);
        visuals.Tick();
        Assert.That(visuals.Animator.Frame, Is.EqualTo(Release));
        visuals.Hold(0.08f);
        visuals.Tick();
        clock.Advance(0.07f);
        visuals.Tick();
        Assert.That(visuals.Animator.Sprite, Is.SameAs(cast.SpriteAt(CharacterDirections.East, Release)), "The hold keeps the release on screen.");
        Assert.That((Vector2)light.transform.localPosition, Is.EqualTo(cast.LightAt(CharacterDirections.East, Release, 0).Offset));
        Assert.That(light.intensity, Is.EqualTo(Intensity).Within(1e-5f), "and its light.");

        typeof(CharacterVisuals).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(visuals, null);
        Assert.That(light.enabled, Is.False, "The light is off while the visuals are.");
    }

    /// <summary>
    /// A cast drawn over the run shows, while running, the light of its frame over the run's frame its legs show, and
    /// standing, the standing clip's.
    /// </summary>
    [Test]
    public void AMovingCast_ShowsTheLightOfItsFrameOverTheGait()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CharacterAnimationClip castRun = set.Find("CastRun");
        CharacterAnimator animator = new(set, locomotion, clock);
        Vector2 running = Vector2.up * 4f;
        animator.Update(running, Vector2.up);
        animator.PlayCast(cast, castRun, Vector2.zero, true, new AttackTimeline(0.3f, 0.06f, 0.24f), 0f, false);
        int litFrames = 0;
        for (int step = 0; step < 40; step++)
        {
            clock.Advance(0.02f);
            animator.Update(running, Vector2.up);
            if (!animator.IsActing)
            {
                break;
            }

            int gaitFrame = (int)(animator.Phase * 8f) % 8;
            CharacterFrameLight expected = castRun.LightAt(CharacterDirections.North, animator.Frame, gaitFrame);
            Assert.That(animator.Light, Is.EqualTo(expected), $"step {step}");
            Assert.That(animator.LightColour, Is.EqualTo(expected.IsLit ? castRun.LightColour : default(Color)), $"step {step}");
            litFrames += expected.IsLit ? 1 : 0;
        }

        Assert.That(litFrames, Is.GreaterThan(0));
        animator.PlayCast(cast, castRun, Vector2.zero, true, new AttackTimeline(0.3f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.3f);
        animator.Update(Vector2.zero, Vector2.up);
        Assert.That(animator.Light, Is.EqualTo(cast.LightAt(CharacterDirections.North, animator.Frame, 0)), "Standing shows the standing clip's light.");
    }

    /// <summary>
    /// Once warm, an effect's light fading and a cast's light following its frames, moving, brightening and fading,
    /// allocate nothing. Turning a Light 2D on or off allocates in the editor only (its sorting layer callbacks), so
    /// the measured stretches are those the light shines through.
    /// </summary>
    [Test]
    public void ShowingSpellLights_AllocatesNothingWhileTheyShine()
    {
        EffectPlayer prefab = CreateEffectPrefab(out _);
        EffectPool pool = new(default);
        CharacterVisuals visuals = CreateVisuals(out Light2D castLight);
        CharacterAnimationClip cast = set.Find("Cast");
        AttackTimeline timeline = new(0.35f, 0.06f, 0.24f);
        EffectPlayer effect = null;

        void Fade()
        {
            for (int step = 0; step < 3; step++)
            {
                effect.Tick(0.04f);
            }
        }

        void FollowTheGlow()
        {
            for (int step = 0; step < 22; step++)
            {
                clock.Advance(0.02f);
                visuals.Tick();
            }
        }

        for (int round = 0; round < 3; round++)
        {
            effect = pool.Play(prefab, 1, 0, Vector2.zero, 0f);
            if (round < 2)
            {
                Fade();
            }
            else
            {
                Assert.That(() => Fade(), Is.Not.AllocatingGCMemory());
            }

            Assert.That(effect.EffectLight.enabled, Is.True);
            effect.Tick(0.04f);

            visuals.PlayCast(cast, null, Vector2.right, false, timeline, 0f, false);
            visuals.Tick();
            clock.Advance(0.1f);
            visuals.Tick();
            Assert.That(visuals.Animator.Frame, Is.EqualTo(1));
            if (round < 2)
            {
                FollowTheGlow();
            }
            else
            {
                Assert.That(() => FollowTheGlow(), Is.Not.AllocatingGCMemory());
            }

            Assert.That(visuals.Animator.Frame, Is.EqualTo(6));
            Assert.That(castLight.enabled, Is.True);
            clock.Advance(0.2f);
            visuals.Tick();
        }
    }

    /// <summary>
    /// The rogue's set carries, for each class cast with a light, the light the baker draws on each frame: where the
    /// glow shines from and how strongly, growing over the gather, full on the release and fading through the ring and
    /// sparks in every direction, in its motion's rim tone; the cast over the run glows as the cast does; no other clip
    /// has a light.
    /// </summary>
    [Test]
    public void TheRoguesCasts_CarryTheLightsTheirGlowIsDrawnWith()
    {
        CharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterAnimationSet rogue = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(RogueSetPath);
        CharacterBaker baker = new(recipe);
        string[] lit = { "CastBolt", "CastMend", "CastSmite" };
        foreach (string name in lit)
        {
            CharacterClip clip = recipe.Clips.First(c => c.Name == name);
            CharacterAnimationClip shipped = rogue.Find(name);
            CharacterFrameLight[] baked = CharacterSheetWriter.Lights(baker.Bake(clip), out Color colour);
            Assert.That(baked, Is.Not.Null, name);
            Assert.That(colour, Is.EqualTo((Color)clip.Motion.Light.Shadow), name);
            Assert.That(shipped.LightColour, Is.EqualTo(colour), name);
            Assert.That(shipped.HasLights, Is.True, name);
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int frame = 0; frame < shipped.FrameCount; frame++)
                {
                    CharacterFrameLight light = shipped.LightAt(direction, frame, 0);
                    CharacterFrameLight drawn = baked[direction * shipped.FrameCount + frame];
                    Assert.That(Vector2.Distance(light.Offset, drawn.Offset), Is.LessThan(1e-5f), $"{name} {direction} {frame}");
                    Assert.That(light.Strength, Is.EqualTo(drawn.Strength).Within(1e-5f), $"{name} {direction} {frame}");
                    Assert.That(light.Strength, Is.EqualTo(GlowStrengths[frame]).Within(1e-5f), $"{name} {direction} {frame}");
                }

                Assert.That(shipped.LightAt(direction, Release, 0).Offset.y, Is.GreaterThan(0.25f), $"{name} {direction}: the release glows at the hand.");
            }
        }

        CharacterAnimationClip smite = rogue.Find("CastSmite");
        CharacterAnimationClip smiteRun = rogue.Find("CastSmiteRun");
        Assert.That(smiteRun.HasLights, Is.True);
        Assert.That(smiteRun.LightColour, Is.EqualTo(smite.LightColour));
        for (int gaitFrame = 0; gaitFrame < smiteRun.GaitFrames; gaitFrame++)
        {
            for (int frame = 0; frame < smiteRun.FrameCount; frame++)
            {
                Assert.That(smiteRun.LightAt(CharacterDirections.South, frame, gaitFrame).Strength, Is.EqualTo(GlowStrengths[frame]).Within(1e-5f), $"over gait frame {gaitFrame}, frame {frame}");
            }
        }

        foreach (CharacterAnimationClip clip in rogue.Clips.Where(c => !lit.Contains(c.Name) && c.Name != "CastSmiteRun"))
        {
            Assert.That(clip.HasLights, Is.False, clip.Name);
        }
    }

    /// <summary>
    /// The impacts of the elemental damage types light what is around them in their palette's middle colour, as their
    /// recipe gives it; Physical and True impacts cast no light.
    /// </summary>
    [Test]
    public void TheImpacts_LightOnlyTheElementalTypes_InTheirMiddleColour()
    {
        ImpactEffectRecipe recipe = AssetDatabase.LoadAssetAtPath<ImpactEffectRecipe>("Assets/Settings/Effects/Impact_Spark.asset");
        EffectSprites sprites = AssetDatabase.LoadAssetAtPath<EffectSprites>(EffectSheetWriter.SpritesPath(recipe));
        DamageType[] elemental = { DamageType.Fire, DamageType.Frost, DamageType.Lightning, DamageType.Poison };
        foreach (DamageType type in System.Enum.GetValues(typeof(DamageType)))
        {
            int variant = (int)type;
            Color expected = elemental.Contains(type) ? (Color)recipe.Palettes[variant].Get(ImpactShade.Middle) : Color.clear;
            Assert.That(recipe.LightColour(variant), Is.EqualTo(expected), type.ToString());
            Assert.That(sprites.LightColour(variant), Is.EqualTo(expected), type.ToString());
        }
    }

    /// <summary>
    /// The player's cast light and the impact's light are point lights on blend style 0, the only one the lit shaders
    /// read, on the lit sorting layers below the bars, without shadows, off until they show, at 1.2 with radii of 3 and
    /// 2; the Weasel and the death puff have none.
    /// </summary>
    [Test]
    public void TheShippedLights_AreSetUpAsTheLitShadersReadThem()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        CharacterVisuals visuals = player.GetComponentInChildren<CharacterVisuals>();
        Assert.That(visuals.CastLight, Is.Not.Null);
        Assert.That(visuals.CastLight.transform.parent, Is.SameAs(visuals.transform));
        Assert.That(new SerializedObject(visuals).FindProperty("castLightIntensity").floatValue, Is.EqualTo(Intensity));
        AssertSpellLight(visuals.CastLight, 3f);

        GameObject impact = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effects/Impact.prefab");
        EffectPlayer effect = impact.GetComponent<EffectPlayer>();
        Assert.That(effect.EffectLight, Is.Not.Null);
        Assert.That(effect.EffectLight.transform.parent, Is.SameAs(impact.GetComponentInChildren<SpriteRenderer>().transform), "The light shines from where the impact is drawn.");
        Assert.That(new SerializedObject(effect).FindProperty("lightIntensity").floatValue, Is.EqualTo(Intensity));
        AssertSpellLight(effect.EffectLight, 2f);

        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Weasel.prefab");
        Assert.That(weasel.GetComponentInChildren<CharacterVisuals>().CastLight, Is.Null);
        Assert.That(weasel.GetComponent<DestroyMobOnDeath>().DeathEffect.EffectLight, Is.Null);
    }

    /// <summary>
    /// Checks a shipped spell light: a point light of <paramref name="radius"/> with URP's default falloff, on blend
    /// style 0 and the Default, Terrain and Entities sorting layers, without shadows or normal maps, off.
    /// </summary>
    private static void AssertSpellLight(Light2D light, float radius)
    {
        Assert.That(light.lightType, Is.EqualTo(Light2D.LightType.Point), light.name);
        Assert.That(light.blendStyleIndex, Is.Zero, light.name);
        Assert.That(light.pointLightOuterRadius, Is.EqualTo(radius), light.name);
        Assert.That(light.pointLightInnerRadius, Is.Zero, light.name);
        Assert.That(light.falloffIntensity, Is.EqualTo(0.5f), light.name);
        Assert.That(light.shadowsEnabled, Is.False, light.name);
        Assert.That(light.enabled, Is.False, $"{light.name} is off until it shows.");
        string[] layers = light.targetSortingLayers.Select(SortingLayer.IDToName).ToArray();
        Assert.That(layers, Is.EquivalentTo(new[] { "Default", "Terrain", "Entities" }), light.name);
    }

    /// <summary>
    /// Character visuals of the test set on the manual clock, with a cast light of <see cref="Intensity"/> on a child.
    /// </summary>
    private CharacterVisuals CreateVisuals(out Light2D light)
    {
        GameObject body = Track(new GameObject("Visuals"));
        body.AddComponent<SpriteRenderer>();
        CharacterVisuals visuals = body.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = locomotion;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);
        GameObject lightObject = new("CastLight");
        lightObject.transform.SetParent(body.transform, false);
        light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.enabled = false;
        visuals.Configure(light, Intensity);
        return visuals;
    }

    /// <summary>
    /// A hidden effect "prefab" (a scene object the pool copies) of two variants, the second lighting in frost blue,
    /// one direction and four frames of 0.04 s, with a light under its sprite.
    /// </summary>
    private EffectPlayer CreateEffectPrefab(out Light2D light)
    {
        EffectSprites effectSprites = Track(EffectSprites.Create(Sprites("Effect", 1).Take(8).ToArray(), 2, 1, 4, 0.04f, new[] { Color.clear, Frost }));
        GameObject root = Track(new GameObject("EffectPrefab"));
        GameObject drawn = new("Sprite");
        drawn.transform.SetParent(root.transform, false);
        SpriteRenderer renderer = drawn.AddComponent<SpriteRenderer>();
        GameObject lightObject = new("Light");
        lightObject.transform.SetParent(drawn.transform, false);
        light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.enabled = false;
        EffectPlayer effect = root.AddComponent<EffectPlayer>();
        effect.Configure(effectSprites, renderer, 0f, light, Intensity);
        renderer.enabled = false;
        return effect;
    }

    /// <summary>
    /// A cast clip of eight frames with its release on frame 4 (a gather of 0.09, 0.09, 0.09 and 0.08 s, a release of
    /// 0.09 s and a follow-through of 0.09, 0.1 and 0.1 s at natural speed) drawn over <paramref name="gaitFrames"/>
    /// gait frames (0 for standing), each sprite glowing at the rogue's strength for its frame from a point of its
    /// own, in frost blue.
    /// </summary>
    private CharacterAnimationClip CastClip(string name, int gaitFrames)
    {
        float[] frameSeconds = { 0.09f, 0.09f, 0.09f, 0.08f, 0.09f, 0.09f, 0.1f, 0.1f };
        Sprite[] sprites = Sprites(name, frameSeconds.Length * Mathf.Max(1, gaitFrames));
        CharacterFrameLight[] lights = new CharacterFrameLight[sprites.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i] = new CharacterFrameLight(new Vector2(0.01f * i, 0.5f + 0.001f * i), GlowStrengths[i % frameSeconds.Length]);
        }

        return CharacterAnimationClip.Create(name, CharacterGait.None, false, sprites, frameSeconds, 0f, Release, gaitFrames, Frost, lights);
    }

    /// <summary>
    /// A sprite of its own for every direction and each of <paramref name="frames"/> frames of clip
    /// <paramref name="name"/>.
    /// </summary>
    private Sprite[] Sprites(string name, int frames)
    {
        Texture2D texture = Track(new Texture2D(4, 4));
        Sprite[] sprites = new Sprite[CharacterDirections.Count * frames];
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
