using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the glow of glowing ground (Docs/Lighting.md, Phase 5): <see cref="TerrainGlowMap"/> full on the glowing
/// cells, falling to nothing at the reach and covering only the cells within it, two maps either side of a border giving
/// the texels one map over both gives, so the glow runs on across chunk borders, and nothing without glowing cells; and a
/// <see cref="TerrainBlendRenderer"/> drawing a material that glows with a Sprite Light 2D over its glow, off and at no
/// strength until it burns, in the material's colour, and none for ground that does not glow.
/// </summary>
public class TerrainGlowMapTests
{
    private const int GroundLayer = 0;
    private const int LavaLayer = 1;
    private const float Reach = 2.5f;

    private readonly List<Object> createdObjects = new();
    private readonly List<System.IDisposable> disposables = new();
    private Tilemap[] layers;
    private Tile tile;
    private GameObject root;

    /// <summary>
    /// Builds a grid with a ground layer filling 32 x 16 cells and a lava layer holding a 4 x 4 pool from (12, 6).
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        root = Track(new GameObject("GlowMapGrid", typeof(Grid)));
        tile = Track(ScriptableObject.CreateInstance<Tile>());
        layers = new Tilemap[2];
        for (int i = 0; i < layers.Length; i++)
        {
            GameObject layerObject = new($"Layer{i}", typeof(Tilemap));
            layerObject.transform.SetParent(root.transform);
            layers[i] = layerObject.GetComponent<Tilemap>();
        }

        Paint(GroundLayer, 0, 0, 31, 15);
        Paint(LavaLayer, 12, 6, 15, 9);
    }

    /// <summary>
    /// Disposes the maps and destroys what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (System.IDisposable disposable in disposables)
        {
            disposable.Dispose();
        }

        disposables.Clear();
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
    /// The glow is full on the pool, falls steadily away from it and is gone past the reach; the map covers the pool and
    /// the reach around it, inside the area it is built for.
    /// </summary>
    [Test]
    public void TheGlow_IsFullOnTheGlowingCells_AndFallsToNothingAtTheReach()
    {
        TerrainGlowMap glow = Build(Map(), 1u << LavaLayer, new RectInt(0, 0, 32, 16));
        Assert.That(glow.IsEmpty, Is.False);
        Assert.That(glow.Area, Is.EqualTo(new RectInt(9, 3, 10, 10)), "The pool and three cells around it, the reach rounded up.");
        Assert.That(At(glow, 13.5f, 7.5f), Is.EqualTo(255), "Full on the pool.");
        byte previous = 255;
        for (float x = 16.125f; x < 16f + Reach; x += 0.25f)
        {
            byte value = At(glow, x, 7.5f);
            Assert.That(value, Is.LessThan(previous), $"Falling at x {x}.");
            previous = value;
        }

        Assert.That(At(glow, 16f + Reach + 0.125f, 7.5f), Is.EqualTo(0), "Nothing past the reach.");
        Assert.That(glow.Texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
        Assert.That(glow.Sprite.pixelsPerUnit, Is.EqualTo(TerrainGlowMap.TexelsPerCell));
    }

    /// <summary>
    /// Two maps built either side of a border through the glow, each over its own cells, give exactly the texels one map
    /// over both gives, so two chunks' glows meet without a seam.
    /// </summary>
    [Test]
    public void TwoMapsEitherSideOfABorder_MatchOneMapOverBoth()
    {
        TerrainControlMap map = Map();
        TerrainGlowMap whole = Build(map, 1u << LavaLayer, new RectInt(0, 0, 32, 16));
        TerrainGlowMap left = Build(map, 1u << LavaLayer, new RectInt(0, 0, 14, 16));
        TerrainGlowMap right = Build(map, 1u << LavaLayer, new RectInt(14, 0, 18, 16));
        for (float y = 3.125f; y < 13f; y += 0.25f)
        {
            for (float x = 9.125f; x < 19f; x += 0.25f)
            {
                TerrainGlowMap part = x < 14f ? left : right;
                Assert.That(At(part, x, y), Is.EqualTo(At(whole, x, y)), $"At ({x}, {y}).");
            }
        }
    }

    /// <summary>
    /// Without a glowing cell within the reach of its area the map is empty.
    /// </summary>
    [Test]
    public void AMapFarFromTheGlow_IsEmpty()
    {
        TerrainControlMap map = Map();
        Assert.That(Build(map, 1u << LavaLayer, new RectInt(22, 0, 10, 16)).IsEmpty, Is.True, "No lava within reach.");
        Assert.That(Build(map, 0u, new RectInt(0, 0, 32, 16)).IsEmpty, Is.True, "No glowing layer.");
    }

    /// <summary>
    /// A renderer drawing a glowing material has a Sprite Light 2D over its glow, its cookie the glow map, in the
    /// material's colour, off and at no strength until it burns, at the strength the material asks; one drawing no
    /// glowing material has none.
    /// </summary>
    [Test]
    public void ARendererDrawingGlowingGround_HasAGlowLight()
    {
        TerrainStyle style = TestTerrainStyle.Create(createdObjects);
        TerrainMaterial ground = TestTerrainStyle.PlainMaterial("Ground", TerrainMaterial.CellMode.None, createdObjects);
        TerrainMaterial lava = TestTerrainStyle.PlainMaterial("Lava", TerrainMaterial.CellMode.None, createdObjects);
        Color colour = new(1f, 0.5f, 0.2f, 1f);
        lava.SetGlow(1.3f, Reach, colour);
        root.SetActive(false);
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[GroundLayer], ground), new TerrainBlendRenderer.Layer(layers[LavaLayer], lava));
        root.SetActive(true);
        Light2D light = renderer.GlowLight;
        Assert.That(light, Is.Not.Null);
        Assert.That(light.lightType, Is.EqualTo(Light2D.LightType.Sprite));
        Assert.That(light.lightCookieSprite, Is.EqualTo(renderer.GlowMap.Sprite));
        Assert.That(light.enabled, Is.False, "Off until it burns.");
        Assert.That(light.intensity, Is.EqualTo(0f));
        Assert.That(light.color, Is.EqualTo(colour));
        Assert.That(light.blendStyleIndex, Is.EqualTo(0));
        Assert.That((Vector2)light.transform.position, Is.EqualTo(new Vector2(9f, 3f)), "On the glow map's first cell.");
        Assert.That(renderer.GlowStrength, Is.EqualTo(1.3f));

        lava.SetGlow(0f, Reach, colour);
        renderer.Rebuild();
        Assert.That(renderer.GlowLight, Is.Null, "Ground that does not glow has no glow light.");
    }

    /// <summary>
    /// A control map over the two layers, disposed in <see cref="TearDown"/>.
    /// </summary>
    private TerrainControlMap Map()
    {
        TerrainControlMap controlMap = new(layers);
        controlMap.Apply();
        disposables.Add(controlMap);
        return controlMap;
    }

    /// <summary>
    /// A glow map over <paramref name="area"/> of <paramref name="glowLayers"/>, disposed in <see cref="TearDown"/>.
    /// </summary>
    private TerrainGlowMap Build(TerrainControlMap controlMap, uint glowLayers, RectInt area)
    {
        TerrainGlowMap glow = new("Test Glow");
        disposables.Add(glow);
        glow.Build(controlMap, glowLayers, area, Reach);
        return glow;
    }

    /// <summary>
    /// The texel of <paramref name="glow"/> at the cell position (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private static byte At(TerrainGlowMap glow, float x, float y)
    {
        return glow.EncodedAt(Mathf.FloorToInt((x - glow.Area.xMin) * TerrainGlowMap.TexelsPerCell), Mathf.FloorToInt((y - glow.Area.yMin) * TerrainGlowMap.TexelsPerCell));
    }

    /// <summary>
    /// Paints <paramref name="layer"/> from (<paramref name="minX"/>, <paramref name="minY"/>) to
    /// (<paramref name="maxX"/>, <paramref name="maxY"/>), inclusive.
    /// </summary>
    private void Paint(int layer, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                layers[layer].SetTile(new Vector3Int(x, y, 0), tile);
            }
        }
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
