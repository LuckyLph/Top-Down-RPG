using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests of crisp light (Docs/Lighting.md's Phase 1). The pixel-lit sprite shader and the sun shadows' caster, cliffs
/// and clouds (Phase 8) shaders compile for D3D without errors or warnings; only the 2D lit passes of the sprite shader and of the
/// terrain graph declare the light keyword; the pixel-lit material is
/// the 2D renderer's default. Rendered through <see cref="TerrainBlendProbe.Render"/>: under a global light alone, a
/// sprite on the pixel-lit material draws exactly the pixels URP's Sprite-Lit-Default draws, and the terrain draws its
/// unlit colour times the light; under a point light, the terrain and sprites (one flipped, one cut from the middle of
/// a larger texture, as the sprite atlas packs props) change light only from one whole art pixel to the next, while the
/// light does fall off across them. And under the shipped day's afternoon sky and sun (Docs/Lighting.md, Phases 3 and
/// 4), an outdoor world's ground and sprites draw exactly the pixels today's light draws wherever no sun shadow falls.
/// At night a sprite's glowing pixels draw at their own colour and flicker to their flicker colour by the glow's flicker
/// (Phase 5). A light stopped by a wall's caster (Phase 7) shadows whole art pixels that hold still as the camera moves,
/// drawn by the 2D renderer's projected shadow shader, ours, which compiles cleanly. Water reflects a light above it on
/// the screen down itself toward the camera (Phase 10), in whole art pixels that move with the water, never above the
/// light; ground that reflects nothing, and water with no light about, draw today's pixels. The cobbles' bevels turn to
/// a point light and to the sun (Phase 11), in whole art pixels, while the afternoon's sun and ground beyond the light's
/// reach draw today's pixels, and a sprite's normals light its side facing a point light. A prop's lamp shadow
/// (Docs/LightingGaps.md, Phase 2) darkens a unit standing in it by column and a prop whole, by the shadow at their
/// feet, and leaves other sprites as they were. Renders from later editor updates, until something is drawn.
/// </summary>
public class PixelLightTests
{
    private const string SpriteMaterialPath = "Assets/Materials/SpritePixelLit.mat";
    private const string SpriteShaderPath = "Assets/Shaders/Sprites/SpritePixelLit.shader";
    private const string CasterShaderPath = "Assets/Shaders/Sprites/SunShadowCaster.shader";
    private const string CliffsShaderPath = "Assets/Shaders/Terrain/SunShadowCliffs.shader";
    private const string CloudsShaderPath = "Assets/Shaders/Lighting/SunShadowClouds.shader";
    private const string LightShadowShaderPath = LightShadowResources.ShaderPath;
    private const string LightKeyword = "USE_SHAPE_LIGHT_TYPE_0";
    private const string LitLightMode = "Universal2D";
    private const string StonePath = "Assets/Settings/Terrain/Materials/Stone.asset";
    private const string UrpLitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
    private const int ArtPixelsPerUnit = 32;
    private const int Zoom = 3;
    private const string UnitsLayer = "Sun Shadow Units";
    private const string PropsLayer = "Sun Shadow Props";
    private static readonly int GlowFlickerId = Shader.PropertyToID("_GlowFlicker");

    private readonly List<Object> createdObjects = new();

    /// <summary>
    /// Destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Shader.SetGlobalVector(GlowFlickerId, Vector4.zero);
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, Vector4.zero);
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, Vector4.zero);
        SunShadowGlobals.Set(Color.black, Vector2.zero, false);
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// The project's lit sprites draw with the pixel-lit material, which has the pixel-lit shader, and the 2D renderer
    /// gives it to every new sprite.
    /// </summary>
    [Test]
    public void TheSpriteMaterial_IsPixelLit_AndTheRenderersDefault()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
        GameObject spriteObject = Track(new GameObject("Sprite"));
        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();

        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo("TopDownRPG/Sprite Pixel Lit"));
        Assert.That(renderer.sharedMaterial, Is.EqualTo(material));
    }

    /// <summary>
    /// The pixel-lit sprite shader, the sun shadows' caster, cliffs and clouds shaders and the lights' projected shadow
    /// shader import without errors or messages, and every pass compiles for D3D, vertex and fragment, without errors or
    /// warnings, with and without the light keyword where the pass declares it.
    /// </summary>
    [TestCase(SpriteShaderPath)]
    [TestCase(CasterShaderPath)]
    [TestCase(CliffsShaderPath)]
    [TestCase(CloudsShaderPath)]
    [TestCase(LightShadowShaderPath)]
    public void TheShader_CompilesForD3DWithoutErrorsOrWarnings(string path)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        Assert.That(shader, Is.Not.Null);
        Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        Assert.That(ShaderUtil.GetShaderMessages(shader), Is.Empty);
        ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
        List<string> problems = new();
        for (int i = 0; i < subshader.PassCount; i++)
        {
            ShaderData.Pass pass = subshader.GetPass(i);
            List<string[]> variants = new() { new string[0] };
            if (DeclaresLightKeyword(shader, i))
            {
                variants.Add(new[] { LightKeyword });
            }

            foreach (string[] keywords in variants)
            {
                foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                {
                    ShaderData.VariantCompileInfo result = pass.CompileVariant(stage, keywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                    string variant = $"pass {i} {stage} [{string.Join(" ", keywords)}]";
                    if (!result.Success)
                    {
                        problems.Add($"{variant} failed");
                    }

                    foreach (ShaderMessage message in result.Messages)
                    {
                        problems.Add($"{variant}: {message.severity} {message.message}");
                    }
                }
            }
        }

        Assert.That(problems, Is.Empty);
    }

    /// <summary>
    /// The light keyword is declared by the 2D lit pass (light mode Universal2D) of the pixel-lit sprite shader and of
    /// the terrain graph, and by no other pass of either, so their other passes have no light variants.
    /// </summary>
    [TestCase(SpriteShaderPath)]
    [TestCase(TestTerrainStyle.ShaderPath)]
    public void OnlyTheLitPass_DeclaresTheLightKeyword(string path)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        int passes = ShaderUtil.GetShaderData(shader).GetSubshader(0).PassCount;
        int lit = 0;
        for (int i = 0; i < passes; i++)
        {
            bool isLit = shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name == LitLightMode;
            lit += isLit ? 1 : 0;
            Assert.That(DeclaresLightKeyword(shader, i), Is.EqualTo(isLit), $"pass {i}");
        }

        Assert.That(lit, Is.EqualTo(1));
    }

    /// <summary>
    /// Under a tinted global light alone, a sprite of many colours and some clear pixels drawn with the pixel-lit
    /// material shows exactly the pixels it shows drawn with URP's Sprite-Lit-Default, at 3 screen pixels per art pixel.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderTheGlobalLightAlone_ALitSprite_DrawsUrpsPixels()
    {
        AddGlobalLight(new Color(1f, 0.8f, 0.6f), 0.85f);
        SpriteRenderer sprite = AddSprite(NoiseTexture(16, 16, 7, true), new Rect(0f, 0f, 16f, 16f), new Vector2(-100f, -100f), false);
        Rect world = new(-100.5f, -100.25f, 1f, 1f);

        sprite.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(UrpLitMaterialPath);
        Color32[] urp = null;
        yield return RenderUntilDrawn(null, world, null, false, pixels => urp = pixels);
        sprite.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
        Color32[] pixelLit = null;
        yield return RenderUntilDrawn(null, world, null, false, pixels => pixelLit = pixels);

        int differing = 0;
        for (int i = 0; i < urp.Length; i++)
        {
            differing += urp[i].Equals(pixelLit[i]) ? 0 : 1;
        }

        Assert.That(differing, Is.EqualTo(0), $"pixels differing of {urp.Length}");
    }

    /// <summary>
    /// Under a global light of (0.75, 0.75, 0) alone, each pixel of a terrain of many colours, read without sRGB
    /// conversion, is its unlit value times the light, to within the rounding of the unlit value to 8 bits.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderTheGlobalLightAlone_TheTerrain_DrawsItsUnlitColourTimesTheLight()
    {
        TerrainBlendRenderer terrain = AddTerrain(NoiseTexture(128, 128, 3, false), 12, 8);
        Rect world = new(2f, 2f, 8f, 4f);
        Color32[] unlit = null;
        yield return RenderUntilDrawn(terrain, world, world.center, true, pixels => unlit = pixels);
        AddGlobalLight(new Color(1f, 1f, 0f), 0.75f);
        Color32[] lit = null;
        yield return RenderUntilDrawn(terrain, world, world.center, true, pixels => lit = pixels);

        int wrong = 0;
        string first = null;
        for (int i = 0; i < unlit.Length; i++)
        {
            bool right = Mathf.Abs(lit[i].r - unlit[i].r * 0.75f) <= 1f && Mathf.Abs(lit[i].g - unlit[i].g * 0.75f) <= 1f && lit[i].b == 0;
            if (!right)
            {
                wrong++;
                first ??= $"pixel {i}: unlit {unlit[i]}, lit {lit[i]}";
            }
        }

        Assert.That(wrong, Is.EqualTo(0), first);
    }

    /// <summary>
    /// A world's global light set by its outdoor lighting to the shipped day's afternoon key, sky and sun together, with
    /// the afternoon's sun casting, draws a terrain of many colours and a sprite casting a shadow on it exactly as the
    /// authored light, white at 0.85, draws them, wherever the sun's shadow does not fall; where it falls the ground is
    /// darker.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderTheAfternoonSky_AnOutdoorWorld_DrawsTodaysPixels()
    {
        TerrainBlendRenderer terrain = AddTerrain(NoiseTexture(128, 128, 5, false), 8, 6);
        SpriteRenderer sprite = AddSprite(NoiseTexture(16, 16, 9, true), new Rect(0f, 0f, 16f, 16f), new Vector2(4f, 2.5f), false);
        sprite.renderingLayerMask |= 1u << RenderingLayerMask.NameToRenderingLayer("Sun Shadow Props");
        Light2D sky = AddGlobalLight(Color.white, 0.85f);
        Rect world = new(2f, 2f, 4f, 2f);
        Color32[] today = null;
        yield return RenderUntilDrawn(terrain, world, world.center, false, pixels => today = pixels);

        DayNightSettings settings = AssetDatabase.LoadAssetAtPath<DayNightSettings>("Assets/Data/Lighting/DayNight.asset");
        Color afternoon = Color.clear;
        for (int i = 0; i < settings.KeyCount; i++)
        {
            Color total = settings.LightAt(settings.GetKey(i).Time).Total;
            if (total.r == 0.85f && total.g == 0.85f && total.b == 0.85f)
            {
                afternoon = total;
            }
        }

        Assert.That(afternoon.a, Is.EqualTo(1f), "The shipped day has today's light as a key.");
        WorldLighting lighting = sky.gameObject.AddComponent<WorldLighting>();
        lighting.ApplySky(afternoon);
        Assert.That(sky.intensity, Is.EqualTo(1f));
        float time = 0f;
        for (int i = 0; i < settings.KeyCount; i++)
        {
            time = settings.LightAt(settings.GetKey(i).Time).Total == afternoon ? settings.GetKey(i).Time : time;
        }

        SunShadowGlobals.Set(settings.LightAt(time).Sun, settings.SunAt(time).Shadow, true);
        Color32[] day = null;
        try
        {
            yield return RenderUntilDrawn(terrain, world, world.center, false, pixels => day = pixels);
        }
        finally
        {
            SunShadowGlobals.Set(Color.black, Vector2.zero, false);
        }

        int differing = 0;
        int shadowed = 0;
        for (int i = 0; i < today.Length; i++)
        {
            bool darker = day[i].r <= today[i].r && day[i].g <= today[i].g && day[i].b <= today[i].b && !today[i].Equals(day[i]);
            shadowed += darker ? 1 : 0;
            differing += today[i].Equals(day[i]) || darker ? 0 : 1;
        }

        Assert.That(differing, Is.EqualTo(0), $"pixels differing other than darker of {today.Length}");
        Assert.That(shadowed, Is.GreaterThan(100), "The sprite casts a shadow on the ground.");
    }

    /// <summary>
    /// Under a white point light over a dim global one, the terrain of one colour and two white sprites standing on it
    /// (one flipped, one cut from the middle of a larger texture) light every art pixel in one value: each 3 x 3 block
    /// of screen pixels is one colour. The light falls off across them: the art pixel under the light is brighter than
    /// one 2.5 units away, and the blocks show many values.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderAPointLight_TheLight_ChangesOnlyAtWholeArtPixels()
    {
        TerrainBlendRenderer terrain = AddTerrain(SolidTexture(128, 128, new Color32(120, 120, 120, 255), true), 16, 12);
        Texture2D white = SolidTexture(64, 64, new Color32(255, 255, 255, 255), false);
        AddSprite(white, new Rect(0f, 0f, 24f, 32f), new Vector2(7f, 6f), true);
        AddSprite(white, new Rect(16f, 8f, 24f, 32f), new Vector2(9f, 6f), false);
        AddGlobalLight(Color.white, 0.2f);
        AddPointLight(new Vector2(8.1f, 6.6f), 3f, 1f);
        Rect world = new(4f, 3f, 8f, 6f);
        Color32[] pixels = null;
        yield return RenderUntilDrawn(terrain, world, new Vector2(5f, 4f), false, drawn => pixels = drawn);

        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        int height = Mathf.RoundToInt(world.height * ArtPixelsPerUnit * Zoom);
        int mixed = 0;
        string first = null;
        HashSet<int> values = new();
        for (int by = 0; by < height; by += Zoom)
        {
            for (int bx = 0; bx < width; bx += Zoom)
            {
                Color32 corner = pixels[by * width + bx];
                values.Add(corner.r << 16 | corner.g << 8 | corner.b);
                for (int i = 0; i < Zoom * Zoom; i++)
                {
                    Color32 pixel = pixels[(by + i / Zoom) * width + bx + i % Zoom];
                    if (!pixel.Equals(corner))
                    {
                        mixed++;
                        first ??= $"art pixel ({bx / Zoom}, {by / Zoom}): {corner} and {pixel}";
                        break;
                    }
                }
            }
        }

        Color32 under = ArtPixel(pixels, width, world, new Vector2(8.1f, 6.2f));
        Color32 away = ArtPixel(pixels, width, world, new Vector2(5.6f, 4.2f));
        Assert.That(mixed, Is.EqualTo(0), first);
        Assert.That(under.r, Is.GreaterThan(away.r + 40));
        Assert.That(values.Count, Is.GreaterThan(40));
    }

    /// <summary>
    /// Water reflects a light standing above it on the screen (Docs/Lighting.md, Phase 10): with its reflections on, the
    /// art pixels below the light, toward the camera, draw brighter than with them off, in whole art pixels, while those
    /// above it and those beside its light's reach draw as with them off; at another time of the water's the reflection
    /// has moved.
    /// </summary>
    [UnityTest]
    public IEnumerator ALightAboveWater_IsReflectedDownIt_InWholeArtPixels_MovingWithTheWater()
    {
        TerrainBlendRenderer terrain = AddTerrain(SolidTexture(128, 128, new Color32(30, 60, 110, 255), true), 16, 12, 1f);
        AddGlobalLight(Color.white, 0.2f);
        AddPointLight(new Vector2(8.1f, 9.6f), 3f, 1.2f);
        Rect world = new(4f, 1f, 8f, 10f);
        Vector2 probe = new(5f, 2f);
        Color32[] off = null;
        Color32[] early = null;
        Color32[] late = null;
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, new Vector4(0.2f, 0.2f, 0.2f, 0f));
        yield return RenderUntilDrawn(terrain, world, probe, false, drawn => off = drawn);
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, new Vector4(0.2f, 0.2f, 0.2f, 1f));
        yield return RenderUntilDrawn(terrain, world, probe, false, drawn => early = drawn);
        terrain.SetFixedTime(0.5f);
        yield return RenderUntilDrawn(terrain, world, probe, false, drawn => late = drawn);

        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        int height = Mathf.RoundToInt(world.height * ArtPixelsPerUnit * Zoom);
        Assert.That(MixedBlocks(early, width, height, out string first), Is.EqualTo(0), first);
        Assert.That(MixedBlocks(late, width, height, out first), Is.EqualTo(0), first);
        int brighter = 0;
        int moved = 0;
        for (float y = 3f; y < 9f; y += 1f / ArtPixelsPerUnit)
        {
            for (float x = 7.2f; x < 9f; x += 1f / ArtPixelsPerUnit)
            {
                Vector2 position = new(x, y);
                Color32 reflected = ArtPixel(early, width, world, position);
                Color32 plain = ArtPixel(off, width, world, position);
                Assert.That(reflected.r, Is.GreaterThanOrEqualTo(plain.r), $"{position}: a reflection only adds light.");
                brighter += reflected.r > plain.r + 20 ? 1 : 0;
                moved += reflected.Equals(ArtPixel(late, width, world, position)) ? 0 : 1;
            }
        }

        Assert.That(brighter, Is.GreaterThan(200), "The light's glitter path runs down the water below it.");
        Assert.That(moved, Is.GreaterThan(50), "It moves with the water.");
        for (float x = 6.5f; x < 9.5f; x += 1f / ArtPixelsPerUnit)
        {
            Vector2 above = new(x, 10.4f);
            Assert.That(ArtPixel(early, width, world, above), Is.EqualTo(ArtPixel(off, width, world, above)), $"{above}: nothing is reflected above the light.");
        }

        for (float y = 2f; y < 9f; y += 1f / ArtPixelsPerUnit)
        {
            Vector2 aside = new(4.5f, y);
            Assert.That(ArtPixel(early, width, world, aside), Is.EqualTo(ArtPixel(off, width, world, aside)), $"{aside}: nor beside the light's reach.");
        }
    }

    /// <summary>
    /// Where nothing reflects, the ground draws today's pixels with water's reflections on (Docs/Lighting.md, Phase 10):
    /// water with no light about but the sky, and ground that reflects nothing under a point light.
    /// </summary>
    [UnityTest]
    public IEnumerator WhereNothingReflects_TheGroundDrawsTodaysPixels()
    {
        TerrainBlendRenderer water = AddTerrain(SolidTexture(128, 128, new Color32(30, 60, 110, 255), true), 16, 12, 1f);
        AddGlobalLight(Color.white, 0.2f);
        Rect world = new(4f, 1f, 8f, 10f);
        Vector2 probe = new(5f, 2f);
        Color32[] off = null;
        Color32[] on = null;
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, new Vector4(0.2f, 0.2f, 0.2f, 0f));
        yield return RenderUntilDrawn(water, world, probe, false, drawn => off = drawn);
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, new Vector4(0.2f, 0.2f, 0.2f, 1f));
        yield return RenderUntilDrawn(water, world, probe, false, drawn => on = drawn);
        Assert.That(on, Is.EqualTo(off), "Water with no light but the sky.");

        Object.DestroyImmediate(water.gameObject);
        TerrainBlendRenderer ground = AddTerrain(SolidTexture(128, 128, new Color32(30, 60, 110, 255), true), 16, 12);
        AddPointLight(new Vector2(8.1f, 9.6f), 3f, 1.2f);
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, new Vector4(0.2f, 0.2f, 0.2f, 0f));
        yield return RenderUntilDrawn(ground, world, probe, false, drawn => off = drawn);
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, new Vector4(0.2f, 0.2f, 0.2f, 1f));
        yield return RenderUntilDrawn(ground, world, probe, false, drawn => on = drawn);
        Assert.That(on, Is.EqualTo(off), "Ground that reflects nothing under a point light.");
    }

    /// <summary>
    /// The project's cobbles turn their bevels to a point light over a night sky (Docs/Lighting.md, Phase 11): with
    /// relief drawn, every art pixel is one colour, the stones near the light change, those whose bevels face it
    /// brighter and those facing away darker than their painted light draws them, and the ground beyond the light's
    /// reach draws as with relief off.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderAPointLight_TheCobblesBevels_TurnToTheLight_InWholeArtPixels()
    {
        TerrainBlendRenderer terrain = AddTerrain(AssetDatabase.LoadAssetAtPath<TerrainMaterial>(StonePath), 16, 12);
        Color sky = new(0.13f, 0.16f, 0.3f);
        AddGlobalLight(sky, 1f);
        AddPointLight(new Vector2(8.1f, 6.6f), 3f, 1f);
        Rect world = new(2f, 2f, 12f, 9f);
        Vector2 probe = new(8f, 6.5f);
        Color32[] off = null;
        Color32[] on = null;
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(sky.r, sky.g, sky.b, 0f));
        yield return RenderUntilDrawn(terrain, world, probe, false, drawn => off = drawn);
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(sky.r, sky.g, sky.b, 1f));
        yield return RenderUntilDrawn(terrain, world, probe, false, drawn => on = drawn);

        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        int height = Mathf.RoundToInt(world.height * ArtPixelsPerUnit * Zoom);
        Assert.That(MixedBlocks(on, width, height, out string first), Is.EqualTo(0), first);
        Color32[] facings = StoneFacings(out int repeat);
        int changed = 0;
        float toward = 0f;
        float away = 0f;
        int towardCount = 0;
        int awayCount = 0;
        for (float y = 5.6f; y < 7.6f; y += 1f / ArtPixelsPerUnit)
        {
            for (float x = 6f; x < 7.6f; x += 1f / ArtPixelsPerUnit)
            {
                Vector2 position = new(x, y);
                int change = Brightness(ArtPixel(on, width, world, position)) - Brightness(ArtPixel(off, width, world, position));
                changed += Mathf.Abs(change) > 12 ? 1 : 0;
                float facing = FacingTowards(facings, repeat, position, Vector2.right);
                if (facing > 0.8f)
                {
                    toward += change;
                    towardCount++;
                }
                else if (facing < -0.8f)
                {
                    away += change;
                    awayCount++;
                }
            }
        }

        Assert.That(changed, Is.GreaterThan(200), "The stones beside the light change.");
        Assert.That(towardCount, Is.GreaterThan(100));
        Assert.That(awayCount, Is.GreaterThan(100));
        Assert.That(toward / towardCount, Is.GreaterThan(4f), "Bevels facing the light brighten.");
        Assert.That(away / awayCount, Is.LessThan(-4f), "Bevels facing away darken.");
        for (float y = 2f; y < 11f; y += 1f / ArtPixelsPerUnit)
        {
            for (float x = 2f; x < 3.5f; x += 1f / ArtPixelsPerUnit)
            {
                Vector2 beyond = new(x, y);
                Assert.That(ArtPixel(on, width, world, beyond), Is.EqualTo(ArtPixel(off, width, world, beyond)), $"{beyond}: beyond the light's reach the painted light stays.");
            }
        }
    }

    /// <summary>
    /// The project's cobbles take their bevels' light from the sun's side (Docs/Lighting.md, Phase 11): under the shipped
    /// day's afternoon sky and sun, which shines within a few degrees of the top left the bevels are painted from, relief
    /// changes no art pixel by more than 4 steps and hardly any by more than one; at sunrise, with the sun low on the other side, the stones' bevels facing the sun
    /// brighten and those facing away darken.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderTheSun_TheCobbles_DrawTheAfternoonsPixels_AndTurnToTheSunrise()
    {
        TerrainBlendRenderer terrain = AddTerrain(AssetDatabase.LoadAssetAtPath<TerrainMaterial>(StonePath), 8, 6);
        DayNightSettings settings = AssetDatabase.LoadAssetAtPath<DayNightSettings>("Assets/Data/Lighting/DayNight.asset");
        float afternoon = -1f;
        float sunrise = -1f;
        for (int i = 0; i < settings.KeyCount; i++)
        {
            float time = settings.GetKey(i).Time;
            Color total = settings.LightAt(time).Total;
            afternoon = total.r == 0.85f && total.g == 0.85f && total.b == 0.85f ? time : afternoon;
            sunrise = sunrise < 0f && settings.SunAt(time).IsUp && settings.LightAt(time).Sun.maxColorComponent > 0.05f ? time : sunrise;
        }

        Assert.That(afternoon, Is.GreaterThanOrEqualTo(0f), "The shipped day has today's light as a key.");
        Assert.That(sunrise, Is.GreaterThanOrEqualTo(0f), "The shipped day has a sunlit key.");
        Light2D sky = AddGlobalLight(Color.white, 1f);
        Rect world = new(2f, 2f, 4f, 2f);
        Color32[][] noon = new Color32[2][];
        Color32[][] dawn = new Color32[2][];
        foreach (float time in new[] { afternoon, sunrise })
        {
            Color total = settings.LightAt(time).Total;
            sky.color = total;
            SunShadowGlobals.Set(settings.LightAt(time).Sun, settings.SunAt(time).Shadow, true);
            for (int drawn = 0; drawn < 2; drawn++)
            {
                Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(total.r, total.g, total.b, drawn));
                Color32[][] into = time == afternoon ? noon : dawn;
                yield return RenderUntilDrawn(terrain, world, world.center, false, pixels => into[drawn] = pixels);
            }
        }

        int stepped = 0;
        int largest = 0;
        for (int i = 0; i < noon[0].Length; i++)
        {
            int difference = Mathf.Max(Mathf.Abs(noon[0][i].r - noon[1][i].r), Mathf.Max(Mathf.Abs(noon[0][i].g - noon[1][i].g), Mathf.Abs(noon[0][i].b - noon[1][i].b)));
            largest = Mathf.Max(largest, difference);
            stepped += difference > 1 ? 1 : 0;
        }

        Assert.That(largest, Is.LessThanOrEqualTo(4), "No pixel moves more than 4 steps at the afternoon.");
        Assert.That(stepped, Is.LessThan(noon[0].Length / 100), $"Hardly any pixel moves more than a step at the afternoon (largest {largest}).");
        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        Vector2 toSun = -settings.SunAt(sunrise).Shadow.normalized;
        Color32[] facings = StoneFacings(out int repeat);
        float toward = 0f;
        float away = 0f;
        int towardCount = 0;
        int awayCount = 0;
        for (float y = world.yMin; y < world.yMax; y += 1f / ArtPixelsPerUnit)
        {
            for (float x = world.xMin; x < world.xMax; x += 1f / ArtPixelsPerUnit)
            {
                Vector2 position = new(x, y);
                int change = Brightness(ArtPixel(dawn[1], width, world, position)) - Brightness(ArtPixel(dawn[0], width, world, position));
                float facing = FacingTowards(facings, repeat, position, toSun);
                if (facing > 0.8f)
                {
                    toward += change;
                    towardCount++;
                }
                else if (facing < -0.8f)
                {
                    away += change;
                    awayCount++;
                }
            }
        }

        Assert.That(toward / towardCount, Is.GreaterThan(2f), "Bevels facing the rising sun brighten.");
        Assert.That(away / awayCount, Is.LessThan(-2f), "Bevels facing away darken.");
    }

    /// <summary>
    /// A sprite's normals (Docs/Lighting.md, Phase 11) light it by where a point light comes from: with relief drawn, its
    /// half facing the light draws brighter and its half facing away darker than with relief off, each art pixel one
    /// colour, while a sprite without normals beside it draws as with relief off; under the night sky alone relief
    /// changes nothing.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderAPointLight_ASpritesNormals_LightItsSideFacingTheLight()
    {
        Color sky = new(0.13f, 0.16f, 0.3f);
        AddGlobalLight(sky, 1f);
        Texture2D albedo = SolidTexture(16, 16, new Color32(150, 150, 150, 255), false);
        Color32[] normalPixels = new Color32[16 * 16];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                normalPixels[y * 16 + x] = x < 8 ? new Color32(0, 128, 128, 255) : new Color32(255, 128, 128, 255);
            }
        }

        Texture2D normals = Texture(16, 16, normalPixels, true);
        Sprite faced = Track(Sprite.Create(albedo, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0f), ArtPixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.zero, false,
            new[] { new SecondarySpriteTexture { name = "_NormalTex", texture = normals } }));
        GameObject facedObject = Track(new GameObject("Faced"));
        facedObject.transform.position = new Vector2(-100f, -100f);
        SpriteRenderer facedSprite = facedObject.AddComponent<SpriteRenderer>();
        facedSprite.sprite = faced;
        facedSprite.sortingLayerName = "Entities";
        facedSprite.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
        AddSprite(albedo, new Rect(0f, 0f, 16f, 16f), new Vector2(-100f, -100.75f), false);
        Rect world = new(-100.25f, -100.75f, 0.5f, 1.25f);
        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        int height = Mathf.RoundToInt(world.height * ArtPixelsPerUnit * Zoom);
        Color32[] skyOff = null;
        Color32[] skyOn = null;
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(sky.r, sky.g, sky.b, 0f));
        yield return RenderUntilDrawn(null, world, null, false, pixels => skyOff = pixels);
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(sky.r, sky.g, sky.b, 1f));
        yield return RenderUntilDrawn(null, world, null, false, pixels => skyOn = pixels);
        Assert.That(skyOn, Is.EqualTo(skyOff), "Under the sky alone relief changes nothing.");

        AddPointLight(new Vector2(-99.2f, -99.9f), 2f, 1f);
        Color32[] off = null;
        Color32[] on = null;
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(sky.r, sky.g, sky.b, 0f));
        yield return RenderUntilDrawn(null, world, null, false, pixels => off = pixels);
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(sky.r, sky.g, sky.b, 1f));
        yield return RenderUntilDrawn(null, world, null, false, pixels => on = pixels);

        Assert.That(MixedBlocks(on, width, height, out string first), Is.EqualTo(0), first);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                Vector2 at = new(-100.25f + (x + 0.5f) / ArtPixelsPerUnit, -100f + (y + 0.5f) / ArtPixelsPerUnit);
                int change = Brightness(ArtPixel(on, width, world, at)) - Brightness(ArtPixel(off, width, world, at));
                if (x < 8)
                {
                    Assert.That(change, Is.LessThan(0), $"({x}, {y}) faces away from the light.");
                }
                else
                {
                    Assert.That(change, Is.GreaterThan(0), $"({x}, {y}) faces the light.");
                }

                Vector2 plain = new(at.x, at.y - 0.75f);
                Assert.That(ArtPixel(on, width, world, plain), Is.EqualTo(ArtPixel(off, width, world, plain)), $"{plain}: a sprite without normals keeps its painted light.");
            }
        }
    }

    /// <summary>
    /// The 2D renderer draws the shadows of lights with our projected shadow shader (Docs/Lighting.md, Phase 7), which
    /// decides each shadow texel by the art pixel it lies in, set in URP's global settings.
    /// </summary>
    [Test]
    public void TheRenderersProjectedShadowShader_IsTheArtGridOne()
    {
        Assert.That(LightShadowResources.Current, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Shader>(LightShadowShaderPath)));
    }

    /// <summary>
    /// A white point light casting shadows over a grey ground under a dim global light, with a wall east of it (a box
    /// Shadow Caster 2D), darkens the ground behind the wall and leaves the ground before it as it was without the wall;
    /// every 3 x 3 block of screen pixels, one art pixel, is one colour, the shadow's slanted edges included; and moved
    /// by quarters of a screen pixel, the camera sees every art pixel in the same colour to within 2 levels, so the
    /// shadow's edge stays on the art grid (Docs/Lighting.md, Phase 7).
    /// </summary>
    [UnityTest]
    public IEnumerator ALightStoppedByAWall_ShadowsWholeArtPixels_ThatHoldStillAsTheCameraMoves()
    {
        TerrainBlendRenderer terrain = AddTerrain(SolidTexture(128, 128, new Color32(150, 150, 150, 255), true), 16, 12);
        AddGlobalLight(Color.white, 0.2f);
        AddPointLight(new Vector2(5.1f, 6.05f), 6f, 1f, true);
        ShadowCaster2D wall = AddWallCaster(new Rect(7f, 5f, 1f, 2.5f));
        Rect world = new(4f, 3f, 8f, 6f);
        Vector2 behind = new(8.6f, 6.25f);
        Vector2 before = new(6.5f, 6.25f);
        wall.enabled = false;
        Color32[] open = null;
        yield return RenderUntilDrawn(terrain, world, before, false, pixels => open = pixels);
        wall.enabled = true;
        wall.Update();
        Color32[] walled = null;
        yield return RenderUntilDrawn(terrain, world, before, false, pixels => walled = pixels);

        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        Assert.That(ArtPixel(walled, width, world, behind).r, Is.LessThan(ArtPixel(open, width, world, behind).r - 20), "The wall's shadow falls behind it.");
        Assert.That(ArtPixel(walled, width, world, before), Is.EqualTo(ArtPixel(open, width, world, before)), "The ground before the wall keeps its light.");
        Assert.That(MixedBlocks(walled, width, Mathf.RoundToInt(world.height * ArtPixelsPerUnit * Zoom), out string first), Is.Zero, first);

        int moved = 0;
        string worst = null;
        foreach (float offset in new[] { 0.25f, 0.5f, 0.75f, 1.25f })
        {
            float shift = offset / (ArtPixelsPerUnit * Zoom);
            Rect shifted = new(world.x + shift, world.y + shift * 0.4f, world.width, world.height);
            Color32[] shiftedPixels = null;
            yield return RenderUntilDrawn(terrain, shifted, before, false, pixels => shiftedPixels = pixels);
            for (float y = world.yMin + 1f; y < world.yMax - 1f; y += 1f / ArtPixelsPerUnit)
            {
                for (float x = world.xMin + 1f; x < world.xMax - 1f; x += 1f / ArtPixelsPerUnit)
                {
                    Vector2 centre = new(x + 0.5f / ArtPixelsPerUnit, y + 0.5f / ArtPixelsPerUnit);
                    Color32 still = ScreenPixel(walled, width, world, centre);
                    Color32 seen = ScreenPixel(shiftedPixels, width, shifted, centre);
                    int change = Mathf.Max(Mathf.Abs(still.r - seen.r), Mathf.Max(Mathf.Abs(still.g - seen.g), Mathf.Abs(still.b - seen.b)));
                    if (change > 2)
                    {
                        moved++;
                        worst ??= $"the art pixel at {centre} moved by {change} levels with the camera {offset} screen pixels on";
                    }
                }
            }
        }

        Assert.That(moved, Is.Zero, worst);
    }

    /// <summary>
    /// A bright point light casting shadows under a dim global light, with a prop's caster north of it shadowing only the
    /// ground's sorting layers, so its shadow runs up the screen (Docs/LightingGaps.md, Phase 2), each sprite compared
    /// with the same render without the caster: a unit standing wholly in the shadow darkens in every pixel; a unit
    /// standing across its edge darkens in its columns over the shadow and keeps the light in those beyond it; a prop
    /// whose feet lie in the shadow darkens whole, its columns beyond the edge too; and a sprite of neither the units'
    /// nor the props' rendering layers keeps every pixel as it was.
    /// </summary>
    [UnityTest]
    public IEnumerator UnderALampsShadow_AUnitDarkensByColumn_APropWhole_AndOtherSpritesKeepTheLight()
    {
        TerrainBlendRenderer terrain = AddTerrain(SolidTexture(128, 128, new Color32(150, 150, 150, 255), true), 16, 12);
        AddGlobalLight(Color.white, 0.2f);
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(0.2f, 0.2f, 0.2f, 0f));
        AddPointLight(new Vector2(8f, 3f), 8f, 3f, true);
        ShadowCaster2D prop = AddWallCaster(new Rect(7.75f, 4.5f, 0.5f, 0.3f));
        SerializedObject serialized = new(prop);
        SerializedProperty sortingLayers = serialized.FindProperty("m_ApplyToSortingLayers");
        sortingLayers.arraySize = 2;
        sortingLayers.GetArrayElementAtIndex(0).intValue = SortingLayer.NameToID("Default");
        sortingLayers.GetArrayElementAtIndex(1).intValue = SortingLayer.NameToID("Terrain");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        prop.Update();

        Texture2D grey = SolidTexture(16, 32, new Color32(150, 150, 150, 255), false);
        Rect rect = new(0f, 0f, 16f, 32f);
        Vector2 inside = new(8f, 6f);
        Vector2 across = new(8.5f, 6f);
        Vector2 propInside = new(8f, 7.3125f);
        Vector2 propAcross = new(8.625f, 7.3125f);
        Vector2 plain = new(8f, 4.875f);
        AddSprite(grey, rect, inside, false).renderingLayerMask = RenderingLayerMask.GetMask(UnitsLayer);
        AddSprite(grey, rect, across, false).renderingLayerMask = RenderingLayerMask.GetMask(UnitsLayer);
        AddSprite(grey, rect, propInside, false).renderingLayerMask = RenderingLayerMask.GetMask(PropsLayer);
        AddSprite(grey, rect, propAcross, false).renderingLayerMask = RenderingLayerMask.GetMask(PropsLayer);
        AddSprite(grey, rect, plain, false);
        Rect world = new(6f, 2f, 5f, 7f);

        prop.enabled = false;
        Color32[] open = null;
        yield return RenderUntilDrawn(terrain, world, inside + new Vector2(0f, 0.5f), false, pixels => open = pixels);
        prop.enabled = true;
        prop.Update();
        Color32[] cast = null;
        yield return RenderUntilDrawn(terrain, world, inside + new Vector2(0f, 0.5f), false, pixels => cast = pixels);

        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        List<string> problems = new();
        for (int row = 0; row < 32; row++)
        {
            for (int column = 0; column < 16; column++)
            {
                Vector2 offset = new((column - 7.5f) / ArtPixelsPerUnit, (row + 0.5f) / ArtPixelsPerUnit);
                ExpectLampShadow(problems, "the unit inside", open, cast, width, world, inside + offset, true);
                ExpectLampShadow(problems, "the prop inside", open, cast, width, world, propInside + offset, true);
                ExpectLampShadow(problems, "the prop across the edge", open, cast, width, world, propAcross + offset, true);
                ExpectLampShadow(problems, "the plain sprite", open, cast, width, world, plain + offset, false);
                if (column >= 1 && column <= 5)
                {
                    ExpectLampShadow(problems, "the unit across the edge, over the shadow", open, cast, width, world, across + offset, true);
                }
                else if (column >= 10 && column <= 14)
                {
                    ExpectLampShadow(problems, "the unit across the edge, beyond it", open, cast, width, world, across + offset, false);
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems.GetRange(0, Mathf.Min(6, problems.Count))));
    }

    /// <summary>
    /// Adds a line to <paramref name="problems"/> naming <paramref name="what"/> when the art pixel holding
    /// <paramref name="position"/> is not darker in <paramref name="cast"/> than in <paramref name="open"/> by more than
    /// 8 levels while <paramref name="darker"/>, or not the same to within one level otherwise.
    /// </summary>
    private static void ExpectLampShadow(List<string> problems, string what, Color32[] open, Color32[] cast, int width, Rect world, Vector2 position, bool darker)
    {
        Color32 before = ArtPixel(open, width, world, position);
        Color32 after = ArtPixel(cast, width, world, position);
        bool holds = darker ? after.r < before.r - 8 : Near(after, before);
        if (!holds)
        {
            problems.Add($"{what} at {position}: {before} without the caster, {after} with it");
        }
    }

    /// <summary>
    /// At night a sprite's glowing pixels draw at their own colour, as unlit, while its other pixels draw as the same
    /// sprite without a glow draws them; with the glow's flicker at a share of one every flickering pixel shows its
    /// flicker colour, and at none its own (Docs/Lighting.md, Phase 5); with the glowing pixels turned off (the lighting
    /// dev panel) every pixel draws as the sprite without a glow draws it, flicker or not.
    /// </summary>
    [UnityTest]
    public IEnumerator AtNight_GlowingPixels_DrawAtTheirOwnColour_FlickerToTheirFlickerColour_AndTurnOff()
    {
        AddGlobalLight(new Color(0.13f, 0.16f, 0.3f), 1f);
        Color32 own = new(200, 120, 40, 255);
        Color32 flicker = new(250, 210, 90, 255);
        Texture2D albedo = SolidTexture(8, 8, own, false);
        Color32[] glowPixels = new Color32[64];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                glowPixels[y * 8 + x] = y >= 4 ? new Color32(flicker.r, flicker.g, flicker.b, 255) : new Color32(0, 0, 0, 255);
            }
        }

        Texture2D glow = Texture(8, 8, glowPixels, false);
        Sprite glowing = Track(Sprite.Create(albedo, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0f), ArtPixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.zero, false,
            new[] { new SecondarySpriteTexture { name = "_GlowTex", texture = glow } }));
        SpriteRenderer sprite = AddSprite(albedo, new Rect(0f, 0f, 8f, 8f), new Vector2(-100f, -100f), false);
        Rect world = new(-100.125f, -100f, 0.25f, 0.25f);
        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        Color32[] plain = null;
        yield return RenderUntilDrawn(null, world, null, false, pixels => plain = pixels);

        sprite.enabled = false;
        GameObject glowingObject = Track(new GameObject("Glowing"));
        glowingObject.transform.position = new Vector2(-100f, -100f);
        SpriteRenderer glowingSprite = glowingObject.AddComponent<SpriteRenderer>();
        glowingSprite.sprite = glowing;
        glowingSprite.sortingLayerName = "Entities";
        glowingSprite.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
        Shader.SetGlobalVector(GlowFlickerId, Vector4.zero);
        Color32[] steady = null;
        yield return RenderUntilDrawn(null, world, null, false, pixels => steady = pixels);
        Shader.SetGlobalVector(GlowFlickerId, new Vector4(3f, 1f, 0f, 0f));
        Color32[] flickering = null;
        yield return RenderUntilDrawn(null, world, null, false, pixels => flickering = pixels);
        Shader.SetGlobalVector(GlowFlickerId, new Vector4(3f, 1f, 1f, 0f));
        Color32[] off = null;
        yield return RenderUntilDrawn(null, world, null, false, pixels => off = pixels);

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Vector2 at = new(world.xMin + (x + 0.5f) / ArtPixelsPerUnit, world.yMin + (y + 0.5f) / ArtPixelsPerUnit);
                Color32 before = ArtPixel(plain, width, world, at);
                Color32 lit = ArtPixel(steady, width, world, at);
                Color32 shown = ArtPixel(flickering, width, world, at);
                Assert.That(ArtPixel(off, width, world, at), Is.EqualTo(before), $"({x}, {y}) draws as without a glow once glowing pixels are off.");
                if (x >= 4)
                {
                    Assert.That(lit, Is.EqualTo(before), $"({x}, {y}) does not glow and draws as before.");
                    Assert.That(shown, Is.EqualTo(before), $"({x}, {y}) does not flicker.");
                    continue;
                }

                Assert.That(Near(lit, own), Is.True, $"({x}, {y}) glows at its own colour, not {lit}.");
                Assert.That(Near(shown, y >= 4 ? flicker : own), Is.True, $"({x}, {y}) shows {shown} at a flicker share of one.");
            }
        }
    }

    /// <summary>
    /// A moving flame's sprite, cut from the bottom row of a sheet of four frames in rows, draws its first frame while
    /// nothing flickers (the editor) and, at each flicker step, the frame <see cref="PropLighting.FlameFrameAt"/> picks
    /// for where it stands, on the flames material's frame count (Docs/Lighting.md, Phase 6); two flames side by side
    /// show their own frames.
    /// </summary>
    [UnityTest]
    public IEnumerator AMovingFlame_DrawsTheFrameItsPlaceAndTheFlickerStepPick()
    {
        const int frames = 4;
        Color32[] colours = { new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255), new(255, 255, 0, 255) };
        Color32[] sheet = new Color32[8 * 8 * frames];
        for (int i = 0; i < sheet.Length; i++)
        {
            sheet[i] = colours[i / 64];
        }

        AddGlobalLight(Color.white, 1f);
        Texture2D texture = Texture(8, 8 * frames, sheet, false);
        Material flames = Track(new Material(AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath)));
        flames.SetFloat("_FlameFrames", frames);
        Vector2[] places = { new(-100f, -100f), new(-99.5f, -100f) };
        foreach (Vector2 place in places)
        {
            AddSprite(texture, new Rect(0f, 0f, 8f, 8f), place, false).sharedMaterial = flames;
        }

        Rect world = new(-100.25f, -100f, 1f, 0.25f);
        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        Color32[] still = null;
        yield return RenderUntilDrawn(null, world, places[0] + new Vector2(0f, 0.1f), false, pixels => still = pixels);
        foreach (Vector2 place in places)
        {
            Assert.That(FrameShown(ArtPixel(still, width, world, place + new Vector2(0f, 0.1f)), colours), Is.EqualTo(0), $"The flame at {place} stands on its first frame.");
        }

        for (int step = 0; step < 6; step++)
        {
            Shader.SetGlobalVector(GlowFlickerId, new Vector4(step, 0.36f, 0f, 0f));
            Color32[] shown = null;
            yield return RenderUntilDrawn(null, world, places[0] + new Vector2(0f, 0.1f), false, pixels => shown = pixels);
            foreach (Vector2 place in places)
            {
                int frame = FrameShown(ArtPixel(shown, width, world, place + new Vector2(0f, 0.1f)), colours);
                Assert.That(frame, Is.EqualTo(PropLighting.FlameFrameAt(place, step, frames)), $"The flame at {place} at step {step}.");
            }
        }

        Assert.That(PropLighting.FlameFrameAt(places[0], 0, frames), Is.Not.EqualTo(PropLighting.FlameFrameAt(places[1], 0, frames)), "The two flames start on frames of their own.");
    }

    /// <summary>
    /// The index of the colour of <paramref name="colours"/> nearest <paramref name="pixel"/>.
    /// </summary>
    private static int FrameShown(Color32 pixel, Color32[] colours)
    {
        int best = -1;
        int nearest = int.MaxValue;
        for (int i = 0; i < colours.Length; i++)
        {
            int distance = Mathf.Abs(pixel.r - colours[i].r) + Mathf.Abs(pixel.g - colours[i].g) + Mathf.Abs(pixel.b - colours[i].b);
            if (distance < nearest)
            {
                nearest = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether pass <paramref name="pass"/> of the first subshader of <paramref name="shader"/> declares the light
    /// keyword.
    /// </summary>
    private static bool DeclaresLightKeyword(Shader shader, int pass)
    {
        PassIdentifier id = new(0, (uint)pass);
        foreach (LocalKeyword keyword in ShaderUtil.GetPassKeywords(shader, in id))
        {
            if (keyword.name == LightKeyword)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> differ by at most one in every channel.
    /// </summary>
    private static bool Near(Color32 a, Color32 b)
    {
        return Mathf.Abs(a.r - b.r) <= 1 && Mathf.Abs(a.g - b.g) <= 1 && Mathf.Abs(a.b - b.b) <= 1;
    }

    /// <summary>
    /// The rendered colour of the art pixel holding <paramref name="position"/>.
    /// </summary>
    private static Color32 ArtPixel(Color32[] pixels, int width, Rect world, Vector2 position)
    {
        int x = Mathf.FloorToInt((position.x - world.xMin) * ArtPixelsPerUnit) * Zoom;
        int y = Mathf.FloorToInt((position.y - world.yMin) * ArtPixelsPerUnit) * Zoom;
        return pixels[y * width + x];
    }

    /// <summary>
    /// Renders <paramref name="world"/> at <see cref="Zoom"/> on later editor updates until the art pixel holding
    /// <paramref name="probe"/> is drawn, or any pixel without a probe, at most 30 updates (the first render after a
    /// shader import draws nothing with it), asserts it was, and hands the pixels to <paramref name="read"/>.
    /// </summary>
    private static IEnumerator RenderUntilDrawn(TerrainBlendRenderer terrain, Rect world, Vector2? probe, bool linear, System.Action<Color32[]> read)
    {
        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * Zoom);
        Color32[] pixels = null;
        bool drawn = false;
        for (int frame = 0; frame < 30 && !drawn; frame++)
        {
            yield return null;
            Texture2D texture = TerrainBlendProbe.Render(terrain, world, Zoom, linear, false);
            pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            if (probe.HasValue)
            {
                Color32 pixel = ArtPixel(pixels, width, world, probe.Value);
                drawn = pixel.r + pixel.g + pixel.b > 0;
            }
            else
            {
                foreach (Color32 pixel in pixels)
                {
                    drawn |= pixel.r + pixel.g + pixel.b > 0;
                }
            }
        }

        Assert.That(drawn, Is.True, probe.HasValue ? $"The art pixel at {probe.Value} is drawn." : "Something is drawn.");
        read(pixels);
    }

    /// <summary>
    /// A grid at the origin whose renderer draws one layer, of a material with <paramref name="albedo"/> reflecting the
    /// lights at <paramref name="reflection"/>, over <paramref name="columns"/> x <paramref name="rows"/> cells.
    /// </summary>
    private TerrainBlendRenderer AddTerrain(Texture2D albedo, int columns, int rows, float reflection = 0f)
    {
        Texture2D height = TestTerrainStyle.CreateTexture("Height", new Color32(128, 128, 0, 255), createdObjects);
        TerrainMaterial material = TerrainMaterial.Create("Ground", albedo, null, 4, TerrainMaterial.CellMode.None, Color.gray, new TerrainStyle.EdgeSettings(), height);
        material.SetReflection(reflection);
        createdObjects.Add(material);
        return AddTerrain(material, columns, rows);
    }

    /// <summary>
    /// A grid at the origin whose renderer draws one layer of <paramref name="material"/> over
    /// <paramref name="columns"/> x <paramref name="rows"/> cells.
    /// </summary>
    private TerrainBlendRenderer AddTerrain(TerrainMaterial material, int columns, int rows)
    {
        TerrainStyle style = TestTerrainStyle.Create(createdObjects);
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        GameObject gridObject = Track(new GameObject("PixelLightGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap ground = new GameObject("Ground", typeof(Tilemap)).GetComponent<Tilemap>();
        ground.transform.SetParent(gridObject.transform);
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(ground, material));
        gridObject.SetActive(true);
        renderer.SetFixedTime(0f);
        return renderer;
    }

    /// <summary>
    /// A sprite on the Entities sorting layer with the pixel-lit material, cut from <paramref name="texture"/> at
    /// <paramref name="rect"/> at 32 pixels per unit with its pivot at its bottom middle, standing at
    /// <paramref name="position"/>, flipped on x when asked.
    /// </summary>
    private SpriteRenderer AddSprite(Texture2D texture, Rect rect, Vector2 position, bool flipX)
    {
        Sprite sprite = Track(Sprite.Create(texture, rect, new Vector2(0.5f, 0f), ArtPixelsPerUnit, 0, SpriteMeshType.FullRect));
        GameObject spriteObject = Track(new GameObject("Sprite"));
        spriteObject.transform.position = position;
        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.flipX = flipX;
        renderer.sortingLayerName = "Entities";
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
        return renderer;
    }

    /// <summary>
    /// A global light on every sorting layer.
    /// </summary>
    private Light2D AddGlobalLight(Color colour, float intensity)
    {
        Light2D light = Track(new GameObject("Global Light 2D")).AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.color = colour;
        light.intensity = intensity;
        return light;
    }

    /// <summary>
    /// A white point light on every sorting layer, of blend style 0, casting hard shadows at full when
    /// <paramref name="casts"/>, its mesh built at once, since a light builds it only in its LateUpdate.
    /// </summary>
    private void AddPointLight(Vector2 position, float radius, float intensity, bool casts = false)
    {
        GameObject lightObject = Track(new GameObject("Point Light 2D"));
        lightObject.transform.position = position;
        Light2D light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = Color.white;
        light.intensity = intensity;
        light.pointLightOuterRadius = radius;
        light.pointLightInnerRadius = 0f;
        light.falloffIntensity = 0.5f;
        light.blendStyleIndex = 0;
        light.shadowsEnabled = casts;
        light.shadowIntensity = 1f;
        light.shadowSoftness = 0f;
        typeof(Light2D).GetMethod("UpdateMesh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(light, new object[] { true });
        typeof(Light2D).GetMethod("UpdateBoundingSphere", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(light, null);
    }

    /// <summary>
    /// A wall's Shadow Caster 2D over <paramref name="box"/>, shaped by URP's shape editor without edge clipping or
    /// trim, as the props' are written, ready to cast.
    /// </summary>
    private ShadowCaster2D AddWallCaster(Rect box)
    {
        ShadowCaster2D caster = Track(new GameObject("Wall")).AddComponent<ShadowCaster2D>();
        SerializedObject serialized = new(caster);
        serialized.FindProperty("m_ShadowCastingSource").intValue = 1;
        SerializedProperty path = serialized.FindProperty("m_ShapePath");
        Vector3[] corners = { new(box.xMin, box.yMin), new(box.xMax, box.yMin), new(box.xMax, box.yMax), new(box.xMin, box.yMax) };
        path.arraySize = corners.Length;
        for (int i = 0; i < corners.Length; i++)
        {
            path.GetArrayElementAtIndex(i).vector3Value = corners[i];
        }

        serialized.FindProperty("m_ShapePathHash").intValue = 1;
        serialized.FindProperty("m_ShadowMesh.m_EdgeProcessing").intValue = 0;
        serialized.FindProperty("m_ShadowMesh.m_TrimEdge").floatValue = 0f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        caster.Update();
        return caster;
    }

    /// <summary>
    /// How many art pixels, blocks of <see cref="Zoom"/> x <see cref="Zoom"/> screen pixels, of a render
    /// <paramref name="width"/> x <paramref name="height"/> are not one colour, naming the first in
    /// <paramref name="first"/>.
    /// </summary>
    private static int MixedBlocks(Color32[] pixels, int width, int height, out string first)
    {
        int mixed = 0;
        first = null;
        for (int by = 0; by < height; by += Zoom)
        {
            for (int bx = 0; bx < width; bx += Zoom)
            {
                Color32 corner = pixels[by * width + bx];
                for (int i = 0; i < Zoom * Zoom; i++)
                {
                    if (!pixels[(by + i / Zoom) * width + bx + i % Zoom].Equals(corner))
                    {
                        mixed++;
                        first ??= $"art pixel ({bx / Zoom}, {by / Zoom}) is not one colour";
                        break;
                    }
                }
            }
        }

        return mixed;
    }

    /// <summary>
    /// The rendered colour of the screen pixel holding <paramref name="position"/> in a render of
    /// <paramref name="world"/>, which need not start on an art pixel.
    /// </summary>
    private static Color32 ScreenPixel(Color32[] pixels, int width, Rect world, Vector2 position)
    {
        int x = Mathf.FloorToInt((position.x - world.xMin) * ArtPixelsPerUnit * Zoom);
        int y = Mathf.FloorToInt((position.y - world.yMin) * ArtPixelsPerUnit * Zoom);
        return pixels[y * width + x];
    }

    /// <summary>
    /// The project's cobbles' height texture's pixels, read from its file, whose alpha holds which way each stone pixel's
    /// bevel faces (Docs/Lighting.md, Phase 11), and its size in pixels, <paramref name="repeat"/>.
    /// </summary>
    private static Color32[] StoneFacings(out int repeat)
    {
        TerrainMaterial stone = AssetDatabase.LoadAssetAtPath<TerrainMaterial>(StonePath);
        Texture2D file = new(2, 2);
        try
        {
            file.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(stone.Height)));
            repeat = file.width;
            return file.GetPixels32();
        }
        finally
        {
            Object.DestroyImmediate(file);
        }
    }

    /// <summary>
    /// How much the bevel of the cobbles' art pixel at <paramref name="position"/>, on a grid at the origin, faces
    /// <paramref name="direction"/>: the cosine between them, 0 where it faces nowhere (the mortar).
    /// </summary>
    private static float FacingTowards(Color32[] facings, int repeat, Vector2 position, Vector2 direction)
    {
        int x = ((Mathf.FloorToInt(position.x * ArtPixelsPerUnit) % repeat) + repeat) % repeat;
        int y = ((Mathf.FloorToInt(position.y * ArtPixelsPerUnit) % repeat) + repeat) % repeat;
        byte code = facings[y * repeat + x].a;
        if (code == 0)
        {
            return 0f;
        }

        float angle = (code - 1) * 2f * Mathf.PI / 255f;
        return Vector2.Dot(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), direction.normalized);
    }

    /// <summary>
    /// A colour's channels summed.
    /// </summary>
    private static int Brightness(Color32 colour)
    {
        return colour.r + colour.g + colour.b;
    }

    /// <summary>
    /// A point-filtered texture of one colour, without sRGB conversion when <paramref name="linear"/> (as a terrain
    /// material's albedo).
    /// </summary>
    private Texture2D SolidTexture(int width, int height, Color32 colour, bool linear)
    {
        Color32[] pixels = new Color32[width * height];
        System.Array.Fill(pixels, colour);
        return Texture(width, height, pixels, linear);
    }

    /// <summary>
    /// A point-filtered texture of colours drawn from a <see cref="System.Random"/> seeded with
    /// <paramref name="seed"/>: a sprite's (sRGB) with about one pixel in eight clear when <paramref name="sprite"/>,
    /// else a terrain material's albedo (linear, opaque).
    /// </summary>
    private Texture2D NoiseTexture(int width, int height, int seed, bool sprite)
    {
        System.Random random = new(seed);
        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)(sprite && random.Next(8) == 0 ? 0 : 255));
        }

        return Texture(width, height, pixels, !sprite);
    }

    /// <summary>
    /// A point-filtered, clamped texture of <paramref name="pixels"/>.
    /// </summary>
    private Texture2D Texture(int width, int height, Color32[] pixels, bool linear)
    {
        Texture2D texture = Track(new Texture2D(width, height, TextureFormat.RGBA32, false, linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Adds <paramref name="created"/> to the objects destroyed in <see cref="TearDown"/>.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
