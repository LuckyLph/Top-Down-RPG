using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the prop prefabs under <c>Assets/Prefabs/Props</c>: the base prefabs <c>Prop</c> and <c>BlockingProp</c>
/// and every variant of them added there, laid out like the player and mob prefabs (the root at the base, a
/// <c>Visuals</c> child on it holding the sprite pivoted on the base, which it sorts by with nothing running, and which
/// casts the sun's shadow), the props fading over their own drawing, the light props carrying their light, off until it
/// burns, and their glow, the footprints of blocking props, and how a placed prop blocks navigation.
/// </summary>
public class PropPrefabTests
{
    private const string PropsFolder = "Assets/Prefabs/Props";
    private const string PropPath = PropsFolder + "/Prop.prefab";
    private const string BlockingPropPath = PropsFolder + "/BlockingProp.prefab";
    private const string VisualsName = "Visuals";
    private const string LightName = "Light";
    private const string LightsFolder = PropsFolder + "/Lights";
    private const string SunShadowPropsLayer = "Sun Shadow Props";
    private const float OnePixel = 1f / 32f;

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object and asset instance the test created.
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
    /// <c>BlockingProp</c> is a variant of <c>Prop</c>, and every other prop prefab is a variant of one of them.
    /// </summary>
    [Test]
    public void EveryProp_IsAVariantOfABaseProp()
    {
        GameObject prop = AssetDatabase.LoadAssetAtPath<GameObject>(PropPath);
        GameObject blocking = AssetDatabase.LoadAssetAtPath<GameObject>(BlockingPropPath);
        Assert.That(prop, Is.Not.Null);
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(blocking), Is.SameAs(prop));

        foreach (GameObject propPrefab in LoadPropPrefabs().Where(p => p != prop && p != blocking))
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(propPrefab);
            Assert.That(source == prop || source == blocking, Is.True, $"{propPrefab.name} should be a variant of Prop or BlockingProp.");
        }
    }

    /// <summary>
    /// Every prop keeps its sprite renderer on a visible <c>Visuals</c> child on the root, as units do, drawn on the
    /// Entities sorting layer at order 0 and sorted by the 2D renderer's custom axis at its sprite's pivot, its base, as
    /// units sort at their feet, with no script on it, so nothing runs when a prop starts; a sprite, where it has one,
    /// pivoted on its base, so the root lies inside it; the root itself draws nothing.
    /// </summary>
    [Test]
    public void Props_KeepTheirSpriteOnAVisualsChild_AtTheirBase()
    {
        int obstacles = LayerMask.NameToLayer("Obstacles");
        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            Assert.That(propPrefab.GetComponent<Renderer>(), Is.Null, $"{propPrefab.name}'s root draws nothing.");
            Transform visuals = Child(propPrefab, VisualsName);
            Assert.That(visuals.localPosition, Is.EqualTo(Vector3.zero), $"{propPrefab.name}'s visuals should sit on its root.");
            Assert.That(visuals.gameObject.layer, Is.Not.EqualTo(obstacles), $"{propPrefab.name}'s visuals must be on a layer the camera draws.");

            SpriteRenderer renderer = visuals.GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null, propPrefab.name);
            Assert.That(renderer.sortingLayerName, Is.EqualTo("Entities"), propPrefab.name);
            if (renderer.sprite != null)
            {
                AssertContainsRoot(renderer.sprite.bounds, $"{propPrefab.name}'s sprite should be pivoted on its base.");
            }

            Assert.That(renderer.sortingOrder, Is.Zero, propPrefab.name);
            Assert.That(renderer.spriteSortPoint, Is.EqualTo(SpriteSortPoint.Pivot), $"{propPrefab.name} should sort at its base.");
            Assert.That(visuals.GetComponents<MonoBehaviour>(), Is.Empty, $"{propPrefab.name}'s visuals run nothing.");
        }
    }

    /// <summary>
    /// Props cast the sun's shadow from their own sprite (Docs/Lighting.md, Phase 4): the base prefabs' <c>Visuals</c>
    /// draw on the props' sun shadow rendering layer besides the default one, every variant's on the same layers or, for
    /// a prop lying flat that casts none, on the default one alone, and no prop draws a shadow of its own any more:
    /// <c>Visuals</c> is its only child, but for a light prop's <c>Light</c>.
    /// </summary>
    [Test]
    public void Props_CastTheSunsShadowFromTheirSprite_AndDrawNoneOfTheirOwn()
    {
        uint casters = 1u << RenderingLayerMask.NameToRenderingLayer(SunShadowPropsLayer);
        uint plain = RenderingLayerMask.defaultRenderingLayerMask.value;
        uint baseLayers = Child(AssetDatabase.LoadAssetAtPath<GameObject>(PropPath), VisualsName).GetComponent<SpriteRenderer>().renderingLayerMask;
        Assert.That(baseLayers, Is.EqualTo(plain | casters));
        Assert.That(Child(AssetDatabase.LoadAssetAtPath<GameObject>(BlockingPropPath), VisualsName).GetComponent<SpriteRenderer>().renderingLayerMask, Is.EqualTo(baseLayers));
        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            int light = propPrefab.transform.Find(LightName) != null ? 1 : 0;
            Assert.That(propPrefab.transform.childCount, Is.EqualTo(1 + light), $"{propPrefab.name} has only its visuals and any light.");
            uint layers = Child(propPrefab, VisualsName).GetComponent<SpriteRenderer>().renderingLayerMask;
            Assert.That(layers == baseLayers || layers == plain, Is.True, $"{propPrefab.name}'s visuals are on rendering layers {layers}.");
        }
    }

    /// <summary>
    /// Every light prop (Docs/Lighting.md, Phase 5) blocks and carries its light in a <c>Light</c> child: a point Light
    /// 2D on blend style 0 and the <c>Default</c>, <c>Terrain</c> and <c>Entities</c> sorting layers, without shadows,
    /// off until it burns, and the <see cref="PropLight"/> that burns it, at a strength above none; its sprite glows,
    /// through its sheet's secondary texture <c>_GlowTex</c>. No other prop has a light.
    /// </summary>
    [Test]
    public void LightProps_CarryTheirLight_OffUntilItBurns_AndGlow()
    {
        List<GameObject> lights = AssetDatabase.FindAssets("t:Prefab", new[] { LightsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .ToList();
        Assert.That(lights.Select(p => p.name), Does.Contain("Campfire_0").And.Contain("LampPost_0"));
        int[] litLayers = { SortingLayer.NameToID("Default"), SortingLayer.NameToID("Terrain"), SortingLayer.NameToID("Entities") };
        foreach (GameObject propPrefab in lights)
        {
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(propPrefab), Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(BlockingPropPath)), $"{propPrefab.name} blocks.");
            Light2D light = Child(propPrefab, LightName).GetComponent<Light2D>();
            Assert.That(light, Is.Not.Null, propPrefab.name);
            Assert.That(light.lightType, Is.EqualTo(Light2D.LightType.Point), propPrefab.name);
            Assert.That(light.blendStyleIndex, Is.EqualTo(0), propPrefab.name);
            Assert.That(light.shadowsEnabled, Is.False, propPrefab.name);
            Assert.That(light.enabled, Is.False, $"{propPrefab.name}'s light is off until it burns.");
            Assert.That(light.targetSortingLayers, Is.EqualTo(litLayers), propPrefab.name);
            PropLight propLight = light.GetComponent<PropLight>();
            Assert.That(propLight, Is.Not.Null, propPrefab.name);
            Assert.That(propLight.LightSource, Is.EqualTo(light), propPrefab.name);
            Assert.That(propLight.Intensity, Is.GreaterThan(0f), propPrefab.name);
            Sprite sprite = Child(propPrefab, VisualsName).GetComponent<SpriteRenderer>().sprite;
            SecondarySpriteTexture[] secondary = new SecondarySpriteTexture[sprite.GetSecondaryTextureCount()];
            sprite.GetSecondaryTextures(secondary);
            Assert.That(secondary.Select(t => t.name), Does.Contain("_GlowTex"), $"{propPrefab.name} glows.");
        }

        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            if (!lights.Contains(propPrefab))
            {
                Assert.That(propPrefab.GetComponentInChildren<PropLight>(true), Is.Null, $"{propPrefab.name} has no light.");
            }
        }
    }

    /// <summary>
    /// Every prop has a fade on its root drawing through its <c>Visuals</c>' renderer; a prop that fades is a tall one,
    /// its fade covering its drawing: around its root and within its sprite.
    /// </summary>
    [Test]
    public void Props_FadeOverTheirOwnDrawing()
    {
        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            Transform visuals = Child(propPrefab, VisualsName);
            PropFade fade = propPrefab.GetComponent<PropFade>();
            Assert.That(fade, Is.Not.Null, propPrefab.name);
            Assert.That(fade.Visuals, Is.EqualTo(visuals.GetComponent<SpriteRenderer>()), propPrefab.name);
            if (!fade.Fades)
            {
                continue;
            }

            Rect cover = fade.Cover;
            Assert.That(cover.yMax, Is.GreaterThan(2f), $"{propPrefab.name} fades, so it stands taller than a unit.");
            AssertContainsRoot(new Bounds(cover.center, cover.size), $"{propPrefab.name}'s fade should cover its root.");
            Bounds drawn = fade.Visuals.sprite.bounds;
            Assert.That(cover.xMin >= drawn.min.x - OnePixel && cover.xMax <= drawn.max.x + OnePixel, Is.True, $"{propPrefab.name}'s fade should lie within its sprite.");
            Assert.That(cover.yMin >= drawn.min.y - OnePixel && cover.yMax <= drawn.max.y + OnePixel, Is.True, $"{propPrefab.name}'s fade should lie within its sprite.");
        }
    }

    /// <summary>
    /// A blocking prop's root is on the Obstacles layer and holds its only collider, a solid box around the root and,
    /// where it has a sprite, lying within the sprite as drawn, like the feet collider on the player's root; a
    /// walk-through prop has no collider at all.
    /// </summary>
    [Test]
    public void BlockingProps_HaveAFootprintAtTheirBase_AndOthersNoCollider()
    {
        GameObject blocking = AssetDatabase.LoadAssetAtPath<GameObject>(BlockingPropPath);
        int obstacles = LayerMask.NameToLayer("Obstacles");
        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            Collider2D[] colliders = propPrefab.GetComponentsInChildren<Collider2D>(true);
            if (propPrefab != blocking && PrefabUtility.GetCorrespondingObjectFromSource(propPrefab) != blocking)
            {
                Assert.That(colliders, Is.Empty, $"{propPrefab.name} is walk-through.");
                continue;
            }

            Assert.That(colliders.Length, Is.EqualTo(1), propPrefab.name);
            BoxCollider2D footprint = colliders[0] as BoxCollider2D;
            Assert.That(footprint, Is.Not.Null, propPrefab.name);
            Assert.That(footprint.gameObject, Is.SameAs(propPrefab), $"{propPrefab.name}'s footprint should be on its root.");
            Assert.That(propPrefab.layer, Is.EqualTo(obstacles), propPrefab.name);
            Assert.That(footprint.isTrigger, Is.False, propPrefab.name);
            Assert.That(footprint.size.x, Is.GreaterThan(0f), propPrefab.name);
            Assert.That(footprint.size.y, Is.GreaterThan(0f), propPrefab.name);
            Bounds box = new(footprint.offset, footprint.size);
            AssertContainsRoot(box, $"{propPrefab.name}'s footprint should lie around its base.");

            Sprite art = Child(propPrefab, VisualsName).GetComponent<SpriteRenderer>().sprite;
            if (art == null)
            {
                continue;
            }

            Bounds drawn = art.bounds;
            Assert.That(box.min.x, Is.GreaterThanOrEqualTo(drawn.min.x - OnePixel), $"{propPrefab.name}'s footprint should lie under its sprite.");
            Assert.That(box.max.x, Is.LessThanOrEqualTo(drawn.max.x + OnePixel), $"{propPrefab.name}'s footprint should lie under its sprite.");
            Assert.That(box.min.y, Is.GreaterThanOrEqualTo(drawn.min.y - OnePixel), $"{propPrefab.name}'s footprint should lie under its sprite.");
            Assert.That(box.max.y, Is.LessThanOrEqualTo(drawn.max.y + OnePixel), $"{propPrefab.name}'s footprint should lie under its sprite.");
        }
    }

    /// <summary>
    /// A blocking prop placed in a cell the way the placement tool snaps it blocks that cell, and only that one, on a
    /// grid whose obstacle layers include Obstacles, while a walk-through prop placed in another cell blocks nothing.
    /// </summary>
    [Test]
    public void APlacedBlockingProp_BlocksItsCell_AndAWalkThroughPropNothing()
    {
        NavigationGrid2D grid = CreateGround(5);
        Place(grid, BlockingPropPath, new Vector2(2.5f, 2.05f));
        Place(grid, PropPath, new Vector2(0.5f, 0.05f));
        grid.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));

        grid.BuildGrid();

        Assert.That(grid.IsCellWalkable(new Vector3Int(2, 2, 0)), Is.False);
        Assert.That(grid.IsCellWalkable(new Vector3Int(1, 2, 0)), Is.True);
        Assert.That(grid.IsCellWalkable(new Vector3Int(3, 2, 0)), Is.True);
        Assert.That(grid.IsCellWalkable(new Vector3Int(2, 1, 0)), Is.True);
        Assert.That(grid.IsCellWalkable(new Vector3Int(2, 3, 0)), Is.True);
        Assert.That(grid.IsCellWalkable(new Vector3Int(0, 0, 0)), Is.True);
    }

    /// <summary>
    /// Every prefab under the props folder, the two base prefabs included.
    /// </summary>
    private static List<GameObject> LoadPropPrefabs()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { PropsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .ToList();
    }

    /// <summary>
    /// The child named <paramref name="childName"/> of <paramref name="propPrefab"/>, failing the test when it has none.
    /// </summary>
    private static Transform Child(GameObject propPrefab, string childName)
    {
        Transform child = propPrefab.transform.Find(childName);
        Assert.That(child, Is.Not.Null, $"{propPrefab.name} should have a {childName} child.");
        return child;
    }

    /// <summary>
    /// Asserts that <paramref name="bounds"/>, in the root's space, hold the root within a pixel.
    /// </summary>
    private static void AssertContainsRoot(Bounds bounds, string message)
    {
        Assert.That(bounds.min.x, Is.LessThanOrEqualTo(OnePixel), message);
        Assert.That(bounds.max.x, Is.GreaterThanOrEqualTo(-OnePixel), message);
        Assert.That(bounds.min.y, Is.LessThanOrEqualTo(OnePixel), message);
        Assert.That(bounds.max.y, Is.GreaterThanOrEqualTo(-OnePixel), message);
    }

    /// <summary>
    /// Builds an unbuilt navigation grid over a <paramref name="size"/> x <paramref name="size"/> square of ground.
    /// </summary>
    private NavigationGrid2D CreateGround(int size)
    {
        GameObject root = Track(new GameObject("PropGrid"));
        root.AddComponent<Grid>();
        GameObject groundObject = new("Ground");
        groundObject.transform.SetParent(root.transform);
        Tilemap ground = groundObject.AddComponent<Tilemap>();
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        groundObject.AddComponent<NavigationTerrainSource2D>().Configure(Track(TerrainType2D.Create("ground")));
        return root.AddComponent<NavigationGrid2D>();
    }

    /// <summary>
    /// Places an instance of the prefab at <paramref name="path"/> where <see cref="PropGridSnapping"/> puts it for a
    /// click at <paramref name="pointer"/> on <paramref name="grid"/>'s cells.
    /// </summary>
    private void Place(NavigationGrid2D grid, string path, Vector2 pointer)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject instance = Track(Object.Instantiate(prefab));
        instance.transform.position = PropGridSnapping.Snap(grid.GetComponent<Grid>(), pointer, prefab);
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
