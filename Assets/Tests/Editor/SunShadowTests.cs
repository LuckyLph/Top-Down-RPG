using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests of the sun's shadows (Docs/Lighting.md, Phase 4). The 2D renderer draws the sun shadow mask with the caster
/// material and the cliffs' shader, the props, the units and the cliffs on their own rendering layers, which the
/// terrain style, the prop prefabs and the units' prefabs use. A camera's mask covers its view on the world's art grid,
/// and draws the props' shadows again only once the view leaves it, the sun moves an art pixel at the tallest shadow's
/// tip, or it is told to. Rendered through <see cref="TerrainBlendProbe.Render"/> with the shipped day's sun: a tree
/// and a unit cast their shadows away from the sun at dawn, noon and dusk and never darken themselves, and a unit
/// standing in the tree's shadow darkens whole; the shadows stay on the world's art pixels as the camera moves by a
/// fraction of one; and raised ground casts its cliffs' shadow away from the sun, and none on itself. Renders from
/// later editor updates, until something is drawn.
/// </summary>
public class SunShadowTests
{
    private const string RendererPath = "Assets/Settings/Renderer2D.asset";
    private const string SpriteMaterialPath = "Assets/Materials/SpritePixelLit.mat";
    private const string DayNightPath = "Assets/Data/Lighting/DayNight.asset";
    private const string PropsLayer = "Sun Shadow Props";
    private const string UnitsLayer = "Sun Shadow Units";
    private const string CliffsLayer = "Sun Shadow Cliffs";
    private const int ArtPixelsPerUnit = 32;
    private const float Dawn = 0.03f;
    private const float Noon = 0.4f;
    private const float Dusk = 0.77f;

    private readonly List<Object> created = new();
    private Light2D sky;

    /// <summary>
    /// Destroys every object the test created and takes the sun away from the shaders.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        SunShadowGlobals.Set(Color.black, Vector2.zero, false);
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// The 2D renderer has the sun shadow feature, drawing casters with the caster material and cliffs with the cliffs'
    /// shader, the props, the units and the cliffs each on their own rendering layer; the terrain style puts grids with
    /// raised ground on the cliffs' layer, and the player's and the Weasel's bodies are on the units'.
    /// </summary>
    [Test]
    public void TheRenderer_DrawsTheSunShadowMask_FromEachCastersOwnLayer()
    {
        SunShadowFeature feature = null;
        foreach (ScriptableRendererFeature rendererFeature in AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath).rendererFeatures)
        {
            feature ??= rendererFeature as SunShadowFeature;
        }

        Assert.That(feature, Is.Not.Null);
        Assert.That(feature.isActive, Is.True);
        Assert.That(feature.CasterMaterial.shader.name, Is.EqualTo("TopDownRPG/Sun Shadow Caster"));
        Assert.That(feature.CliffShader.name, Is.EqualTo("TopDownRPG/Terrain/Sun Shadow Cliffs"));
        Assert.That(feature.PropLayers.value, Is.EqualTo(Layer(PropsLayer)));
        Assert.That(feature.UnitLayers.value, Is.EqualTo(Layer(UnitsLayer)));
        Assert.That(feature.CliffLayers.value, Is.EqualTo(Layer(CliffsLayer)));
        Assert.That(AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset").CliffShadowLayers.value, Is.EqualTo(Layer(CliffsLayer)));
        foreach (string unit in new[] { "Assets/Prefabs/Player/Player.prefab", "Assets/Prefabs/Mobs/Weasel.prefab" })
        {
            SpriteRenderer body = AssetDatabase.LoadAssetAtPath<GameObject>(unit).transform.Find("Visuals").GetComponent<SpriteRenderer>();
            Assert.That(body.renderingLayerMask, Is.EqualTo(RenderingLayerMask.defaultRenderingLayerMask.value | Layer(UnitsLayer)), unit);
        }
    }

    /// <summary>
    /// A camera's mask covers its view, the feet of casters up to the tallest's height below it and a margin, on whole
    /// art pixels; it draws nothing while the sun casts nothing or the camera is not orthographic. Once the props' shadows
    /// are drawn they stay current while the camera moves within the margin and the sun's shadow vector moves less than
    /// an art pixel at the tallest caster's tip, and go out of date when the view leaves the area (which is centred on it
    /// again), the sun moves that far, or the mask is told to.
    /// </summary>
    [Test]
    public void TheMask_FollowsTheViewOnTheArtGrid_AndRedrawsThePropsOnlyWhenNeeded()
    {
        Camera camera = Track(new GameObject("Camera")).AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5.625f;
        camera.aspect = 16f / 9f;
        camera.transform.position = new Vector3(100.3f, 50.7f, -10f);
        SunShadowMask mask = new(camera);
        try
        {
            Vector2 shadow = new(1f, -0.5f);
            Assert.That(mask.Follow(shadow, false, 5f, 4f, 1f), Is.False, "No sun, no mask.");
            Assert.That(mask.Follow(shadow, true, 5f, 4f, 1f), Is.True);
            Assert.That(mask.PropsCurrent, Is.False);
            RectInt area = mask.Area;
            Assert.That(area.xMin, Is.LessThanOrEqualTo(Mathf.FloorToInt((100.3f - 10f - 4f) * ArtPixelsPerUnit)));
            Assert.That(area.xMax, Is.GreaterThanOrEqualTo(Mathf.CeilToInt((100.3f + 10f + 4f) * ArtPixelsPerUnit)));
            Assert.That(area.yMin, Is.LessThanOrEqualTo(Mathf.FloorToInt((50.7f - 5.625f - 5f - 4f) * ArtPixelsPerUnit)));
            Assert.That(area.yMax, Is.GreaterThanOrEqualTo(Mathf.CeilToInt((50.7f + 5.625f + 4f) * ArtPixelsPerUnit)));
            Assert.That(mask.Props.rt.width, Is.EqualTo(area.width));
            Assert.That(mask.Props.rt.height, Is.EqualTo(area.height));
            Assert.That(mask.ShaderRect, Is.EqualTo(new Vector4(area.xMin, area.yMin, area.width, area.height)));

            mask.MarkPropsDrawn();
            camera.transform.position += new Vector3(2.5f, -1.5f, 0f);
            Vector2 nudged = shadow + new Vector2(0.5f / (5f * ArtPixelsPerUnit), 0f);
            Assert.That(mask.Follow(nudged, true, 5f, 4f, 1f), Is.True);
            Assert.That(mask.PropsCurrent, Is.True, "Within the margin and under an art pixel of sun, the props' shadows hold.");
            Assert.That(mask.Area, Is.EqualTo(area));

            Vector2 moved = shadow + new Vector2(1.5f / (5f * ArtPixelsPerUnit), 0f);
            mask.Follow(moved, true, 5f, 4f, 1f);
            Assert.That(mask.PropsCurrent, Is.False, "The sun moved an art pixel and more at the tallest tip.");

            mask.MarkPropsDrawn();
            camera.transform.position += new Vector3(8f, 0f, 0f);
            mask.Follow(moved, true, 5f, 4f, 1f);
            Assert.That(mask.PropsCurrent, Is.False, "The view left the area.");
            Assert.That(mask.Area.xMin, Is.GreaterThan(area.xMin));

            mask.MarkPropsDrawn();
            mask.Invalidate();
            Assert.That(mask.PropsCurrent, Is.False);

            camera.orthographic = false;
            Assert.That(mask.Follow(moved, true, 5f, 4f, 1f), Is.False, "A perspective camera draws no mask.");
        }
        finally
        {
            mask.Dispose();
        }
    }

    /// <summary>
    /// At dawn, noon and dusk under the shipped day, a tree (a column three units tall on the props' layer) and a unit
    /// (one unit tall on the units' layer) shadow the ground where the sun's shadow vector lays their sprites, and not
    /// the ground beside them away from it; neither darkens itself. At dusk a unit standing in the tree's shadow darkens
    /// in every pixel. Each render is compared with the same light but the sun casting nothing.
    /// </summary>
    [UnityTest]
    public IEnumerator ATreeAndAUnit_CastTheirShadowsAwayFromTheSun_AndNeverDarkenThemselves()
    {
        DayNightSettings day = AssetDatabase.LoadAssetAtPath<DayNightSettings>(DayNightPath);
        TerrainBlendRenderer terrain = AddTerrain(20, 12, null);
        Vector2 treeFeet = new(8f, 6f);
        Vector2 unitFeet = new(15f, 6f);
        AddCaster(SolidTexture(16, 96, new Color32(200, 120, 60, 255)), treeFeet, PropsLayer);
        SpriteRenderer unit = AddCaster(SolidTexture(12, 32, new Color32(180, 60, 60, 255)), unitFeet, UnitsLayer);
        sky = AddGlobalLight();
        Rect world = new(0f, 0f, 20f, 12f);
        foreach (float time in new[] { Dawn, Noon, Dusk })
        {
            SunState sun = day.SunAt(time);
            Color32[] cast = null;
            Color32[] open = null;
            yield return RenderUnderSun(terrain, world, day, time, true, pixels => cast = pixels);
            yield return RenderUnderSun(terrain, world, day, time, false, pixels => open = pixels);

            Vector2 away = new(sun.Shadow.x != 0f ? -Mathf.Sign(sun.Shadow.x) : -1f, 0f);
            Assert.That(IsDarker(cast, open, world, treeFeet + sun.Shadow * 1.5f), Is.True, $"{time}: the tree's shadow");
            Assert.That(Same(cast, open, world, treeFeet + new Vector2(away.x * 1.5f, sun.Shadow.y * 1.5f)), Is.True, $"{time}: the ground beside the tree, away from its shadow");
            Assert.That(IsDarker(cast, open, world, unitFeet + sun.Shadow * 0.5f), Is.True, $"{time}: the unit's shadow");
            Assert.That(Same(cast, open, world, treeFeet + new Vector2(0f, 1.5f)), Is.True, $"{time}: the tree lit");
            Assert.That(Same(cast, open, world, unitFeet + new Vector2(0f, 0.5f)), Is.True, $"{time}: the unit lit");
        }

        Vector2 inShadow = treeFeet + day.SunAt(Dusk).Shadow * 2f;
        unit.transform.position = inShadow;
        Color32[] shaded = null;
        Color32[] lit = null;
        yield return RenderUnderSun(terrain, world, day, Dusk, true, pixels => shaded = pixels);
        yield return RenderUnderSun(terrain, world, day, Dusk, false, pixels => lit = pixels);
        int darker = 0;
        int pixelsOfUnit = 0;
        for (int y = 1; y < 31; y++)
        {
            for (int x = -5; x < 5; x++)
            {
                Vector2 point = inShadow + new Vector2((x + 0.5f) / ArtPixelsPerUnit, (y + 0.5f) / ArtPixelsPerUnit);
                pixelsOfUnit++;
                darker += IsDarker(shaded, lit, world, point) ? 1 : 0;
            }
        }

        Assert.That(darker, Is.EqualTo(pixelsOfUnit), "A unit in the tree's shadow darkens whole.");
    }

    /// <summary>
    /// At dusk, the ground's shadowed and lit art pixels around a tree are the same whether the camera stands on the art
    /// grid or a third of an art pixel off it, and on the grid every art pixel there is one colour across its 3 x 3
    /// screen pixels.
    /// </summary>
    [UnityTest]
    public IEnumerator TheShadows_StayOnTheWorldsArtPixels_AsTheCameraMoves()
    {
        DayNightSettings day = AssetDatabase.LoadAssetAtPath<DayNightSettings>(DayNightPath);
        TerrainBlendRenderer terrain = AddTerrain(20, 12, null);
        Vector2 treeFeet = new(6f, 8f);
        AddCaster(SolidTexture(16, 96, new Color32(200, 120, 60, 255)), treeFeet, PropsLayer);
        sky = AddGlobalLight();
        Rect onGrid = new(5f, 2f, 10f, 6f);
        Rect offGrid = new(5f + 1f / 96f, 2f + 1f / 96f, 10f, 6f);
        Color32[] first = null;
        Color32[] moved = null;
        Color32[] open = null;
        yield return RenderUnderSun(terrain, onGrid, day, Dusk, true, pixels => first = pixels, 3);
        yield return RenderUnderSun(terrain, offGrid, day, Dusk, true, pixels => moved = pixels, 3);
        yield return RenderUnderSun(terrain, onGrid, day, Dusk, false, pixels => open = pixels, 3);

        int width = 10 * ArtPixelsPerUnit * 3;
        int shadowed = 0;
        int sunlit = 0;
        int mixed = 0;
        int differing = 0;
        for (int j = 4 * ArtPixelsPerUnit; j < 7 * ArtPixelsPerUnit; j++)
        {
            for (int i = 7 * ArtPixelsPerUnit; i < 14 * ArtPixelsPerUnit; i++)
            {
                Vector2 point = new((i + 0.5f) / ArtPixelsPerUnit, (j + 0.5f) / ArtPixelsPerUnit);
                Color32 here = Pixel(first, onGrid, point, 3);
                differing += here.Equals(Pixel(moved, offGrid, point, 3)) ? 0 : 1;
                bool dark = !here.Equals(Pixel(open, onGrid, point, 3));
                shadowed += dark ? 1 : 0;
                sunlit += dark ? 0 : 1;
                int bx = (i - 5 * ArtPixelsPerUnit) * 3;
                int by = (j - 2 * ArtPixelsPerUnit) * 3;
                for (int k = 0; k < 9; k++)
                {
                    if (!first[(by + k / 3) * width + bx + k % 3].Equals(first[by * width + bx]))
                    {
                        mixed++;
                        break;
                    }
                }
            }
        }

        Assert.That(shadowed, Is.GreaterThan(200), "The window holds some of the tree's shadow.");
        Assert.That(sunlit, Is.GreaterThan(200), "The window holds some sunlit ground.");
        Assert.That(differing, Is.EqualTo(0), "Moving the camera moves no shadow against the ground.");
        Assert.That(mixed, Is.EqualTo(0), "Each art pixel is one colour.");
    }

    /// <summary>
    /// Raised ground four cells square, its cliff face the style's 64 px, shadows the level ground beside its right side
    /// at dusk, and below its face at noon down to the face's height times the noon length and no further; never the
    /// ground on the side toward the sun, nor its own top. Each render is compared with the same light but the sun
    /// casting nothing.
    /// </summary>
    [UnityTest]
    public IEnumerator RaisedGround_CastsItsCliffsShadowAwayFromTheSun_AndNoneOnItself()
    {
        DayNightSettings day = AssetDatabase.LoadAssetAtPath<DayNightSettings>(DayNightPath);
        TerrainBlendRenderer terrain = AddTerrain(16, 12, new RectInt(6, 6, 4, 4));
        sky = AddGlobalLight();
        Rect world = new(0f, 0f, 16f, 12f);
        Color32[] cast = null;
        Color32[] open = null;
        yield return RenderUnderSun(terrain, world, day, Dusk, true, pixels => cast = pixels);
        yield return RenderUnderSun(terrain, world, day, Dusk, false, pixels => open = pixels);
        Assert.That(IsDarker(cast, open, world, new Vector2(10.5f, 6.5f)), Is.True, "The ground right of the cliff at dusk.");
        Assert.That(Same(cast, open, world, new Vector2(5.5f, 6.5f)), Is.True, "The ground left of the cliff, toward the sun.");
        Assert.That(Same(cast, open, world, new Vector2(8f, 9f)), Is.True, "The top of the raised ground.");

        float reach = day.SunAt(Noon).Shadow.y * 2f;
        yield return RenderUnderSun(terrain, world, day, Noon, true, pixels => cast = pixels);
        yield return RenderUnderSun(terrain, world, day, Noon, false, pixels => open = pixels);
        Assert.That(IsDarker(cast, open, world, new Vector2(8f, 6f + reach * 0.5f)), Is.True, "The ground below the face at noon.");
        Assert.That(Same(cast, open, world, new Vector2(8f, 6f + reach * 1.5f)), Is.True, "The ground past the noon shadow.");
        Assert.That(Same(cast, open, world, new Vector2(8f, 7f)), Is.True, "The cliff face.");
    }

    /// <summary>
    /// The bit of the rendering layer named <paramref name="name"/>.
    /// </summary>
    private static uint Layer(string name)
    {
        int index = RenderingLayerMask.NameToRenderingLayer(name);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"The rendering layer {name} is defined.");
        return 1u << index;
    }

    /// <summary>
    /// Renders <paramref name="world"/> at <paramref name="zoom"/> under the day's light at <paramref name="time"/>, the
    /// global light the sky and the sun together, the sun casting its shadows when <paramref name="casts"/>, on later
    /// editor updates until anything is drawn, and hands the pixels to <paramref name="read"/>.
    /// </summary>
    private IEnumerator RenderUnderSun(TerrainBlendRenderer terrain, Rect world, DayNightSettings day, float time, bool casts, System.Action<Color32[]> read, int zoom = 1)
    {
        DayLight light = day.LightAt(time);
        SunState sun = day.SunAt(time);
        sky.color = light.Total;
        sky.intensity = 1f;
        SunShadowGlobals.Set(light.Sun, sun.Shadow, casts && sun.IsUp);
        Color32[] pixels = null;
        bool drawn = false;
        for (int frame = 0; frame < 30 && !drawn; frame++)
        {
            yield return null;
            Texture2D texture = TerrainBlendProbe.Render(terrain, world, zoom, false, false);
            pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            foreach (Color32 pixel in pixels)
            {
                drawn |= pixel.r + pixel.g + pixel.b > 0;
            }
        }

        Assert.That(drawn, Is.True, "Something is drawn.");
        read(pixels);
    }

    /// <summary>
    /// Whether the art pixel at <paramref name="point"/> is darker in <paramref name="cast"/> than in
    /// <paramref name="open"/> in every channel, and strictly in one.
    /// </summary>
    private static bool IsDarker(Color32[] cast, Color32[] open, Rect world, Vector2 point)
    {
        Color32 a = Pixel(cast, world, point, 1);
        Color32 b = Pixel(open, world, point, 1);
        return a.r <= b.r && a.g <= b.g && a.b <= b.b && (a.r < b.r || a.g < b.g || a.b < b.b);
    }

    /// <summary>
    /// Whether the art pixel at <paramref name="point"/> is the same in both renders.
    /// </summary>
    private static bool Same(Color32[] cast, Color32[] open, Rect world, Vector2 point)
    {
        return Pixel(cast, world, point, 1).Equals(Pixel(open, world, point, 1));
    }

    /// <summary>
    /// The rendered colour of the art pixel holding <paramref name="point"/>, rendered at <paramref name="zoom"/>.
    /// </summary>
    private static Color32 Pixel(Color32[] pixels, Rect world, Vector2 point, int zoom)
    {
        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * zoom);
        int x = Mathf.FloorToInt((point.x - world.xMin) * ArtPixelsPerUnit * zoom);
        int y = Mathf.FloorToInt((point.y - world.yMin) * ArtPixelsPerUnit * zoom);
        return pixels[y * width + x];
    }

    /// <summary>
    /// A grid at the origin whose renderer draws one plain layer over <paramref name="columns"/> x
    /// <paramref name="rows"/> cells, with raised ground over <paramref name="raised"/> when given, on the style's cliff
    /// layers, its animation at a fixed time.
    /// </summary>
    private TerrainBlendRenderer AddTerrain(int columns, int rows, RectInt? raised)
    {
        TerrainMaterial material = TestTerrainStyle.PlainMaterial("Ground", TerrainMaterial.CellMode.None, created);
        TerrainStyle style = TestTerrainStyle.Create(created);
        SerializedObject settings = new(style);
        settings.FindProperty("cliffShadowLayers").FindPropertyRelative("m_Bits").uintValue = Layer(CliffsLayer);
        settings.FindProperty("cliffFringe").floatValue = 0f;
        settings.FindProperty("cliffPebbleChance").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        GameObject gridObject = Track(new GameObject("SunShadowGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap ground = new GameObject("Ground", typeof(Tilemap)).GetComponent<Tilemap>();
        ground.transform.SetParent(gridObject.transform);
        Tilemap raisedMap = null;
        if (raised.HasValue)
        {
            raisedMap = new GameObject("Raised", typeof(Tilemap)).GetComponent<Tilemap>();
            raisedMap.transform.SetParent(gridObject.transform);
        }

        RaisedGroundTile raisedTile = Track(RaisedGroundTile.Create(false));
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), tile);
                if (raised.HasValue && raised.Value.Contains(new Vector2Int(x, y)))
                {
                    raisedMap.SetTile(new Vector3Int(x, y, 0), raisedTile);
                }
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, raisedMap, new TerrainBlendRenderer.Layer(ground, material));
        gridObject.SetActive(true);
        renderer.SetFixedTime(0f);
        return renderer;
    }

    /// <summary>
    /// A sprite of <paramref name="texture"/> with the pixel-lit material on the Entities sorting layer, pivoted on its
    /// bottom middle at 32 pixels per unit and standing at <paramref name="feet"/>, on the default rendering layer and
    /// the one named <paramref name="layer"/>.
    /// </summary>
    private SpriteRenderer AddCaster(Texture2D texture, Vector2 feet, string layer)
    {
        Sprite sprite = Track(Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0f), ArtPixelsPerUnit, 0, SpriteMeshType.FullRect));
        GameObject casterObject = Track(new GameObject("Caster"));
        casterObject.transform.position = feet;
        SpriteRenderer renderer = casterObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = "Entities";
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
        renderer.renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask.value | Layer(layer);
        return renderer;
    }

    /// <summary>
    /// A global light on every sorting layer.
    /// </summary>
    private Light2D AddGlobalLight()
    {
        Light2D light = Track(new GameObject("Global Light 2D")).AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        return light;
    }

    /// <summary>
    /// A point-filtered, clamped sprite texture of one colour.
    /// </summary>
    private Texture2D SolidTexture(int width, int height, Color32 colour)
    {
        Color32[] pixels = new Color32[width * height];
        System.Array.Fill(pixels, colour);
        Texture2D texture = Track(new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Adds <paramref name="instance"/> to the objects destroyed in <see cref="TearDown"/>.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
