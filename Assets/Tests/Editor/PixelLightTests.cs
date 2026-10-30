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
/// Tests of crisp light (Docs/Lighting.md's Phase 1). The pixel-lit sprite shader and the sun shadows' caster and
/// cliffs shaders compile for D3D without errors or warnings; only the 2D lit passes of the sprite shader and of the
/// terrain graph declare the light keyword; the pixel-lit material is
/// the 2D renderer's default. Rendered through <see cref="TerrainBlendProbe.Render"/>: under a global light alone, a
/// sprite on the pixel-lit material draws exactly the pixels URP's Sprite-Lit-Default draws, and the terrain draws its
/// unlit colour times the light; under a point light, the terrain and sprites (one flipped, one cut from the middle of
/// a larger texture, as the sprite atlas packs props) change light only from one whole art pixel to the next, while the
/// light does fall off across them. And under the shipped day's afternoon sky and sun (Docs/Lighting.md, Phases 3 and
/// 4), an outdoor world's ground and sprites draw exactly the pixels today's light draws wherever no sun shadow falls.
/// Renders from later editor updates, until something is drawn.
/// </summary>
public class PixelLightTests
{
    private const string SpriteMaterialPath = "Assets/Materials/SpritePixelLit.mat";
    private const string SpriteShaderPath = "Assets/Shaders/Sprites/SpritePixelLit.shader";
    private const string CasterShaderPath = "Assets/Shaders/Sprites/SunShadowCaster.shader";
    private const string CliffsShaderPath = "Assets/Shaders/Terrain/SunShadowCliffs.shader";
    private const string LightKeyword = "USE_SHAPE_LIGHT_TYPE_0";
    private const string LitLightMode = "Universal2D";
    private const string UrpLitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
    private const int ArtPixelsPerUnit = 32;
    private const int Zoom = 3;

    private readonly List<Object> createdObjects = new();

    /// <summary>
    /// Destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
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
    /// The pixel-lit sprite shader and the sun shadows' caster and cliffs shaders import without errors or messages,
    /// and every pass compiles for D3D, vertex and fragment, without errors or warnings, with and without the light
    /// keyword where the pass declares it.
    /// </summary>
    [TestCase(SpriteShaderPath)]
    [TestCase(CasterShaderPath)]
    [TestCase(CliffsShaderPath)]
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
    /// A grid at the origin whose renderer draws one layer, of a material with <paramref name="albedo"/>, over
    /// <paramref name="columns"/> x <paramref name="rows"/> cells.
    /// </summary>
    private TerrainBlendRenderer AddTerrain(Texture2D albedo, int columns, int rows)
    {
        Texture2D height = TestTerrainStyle.CreateTexture("Height", new Color32(128, 128, 0, 255), createdObjects);
        TerrainMaterial material = TerrainMaterial.Create("Ground", albedo, null, 4, TerrainMaterial.CellMode.None, Color.gray, new TerrainStyle.EdgeSettings(), height);
        createdObjects.Add(material);
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
    /// A white point light on every sorting layer, of blend style 0, its mesh built at once, since a light builds it
    /// only in its LateUpdate.
    /// </summary>
    private void AddPointLight(Vector2 position, float radius, float intensity)
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
        typeof(Light2D).GetMethod("UpdateMesh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(light, new object[] { true });
        typeof(Light2D).GetMethod("UpdateBoundingSphere", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(light, null);
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
