using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the prop prefabs under <c>Assets/Prefabs/Props</c>: the base prefabs <c>Prop</c> and <c>BlockingProp</c>
/// and every variant of them added there, laid out like the player and mob prefabs (the root at the base, a
/// <c>Visuals</c> child on it holding the sprite pivoted on the base and its sorter, and a <c>Shadow</c> child drawing
/// the shadow on the ground under every unit), the footprints of blocking props, and how a placed prop blocks
/// navigation.
/// </summary>
public class PropPrefabTests
{
    private const string PropsFolder = "Assets/Prefabs/Props";
    private const string PropPath = PropsFolder + "/Prop.prefab";
    private const string BlockingPropPath = PropsFolder + "/BlockingProp.prefab";
    private const string VisualsName = "Visuals";
    private const string ShadowName = "Shadow";
    private const string ShadowSortingLayer = "Terrain";
    private const int ShadowSortingOrder = 5;
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
    /// Every prop keeps its sprite renderer and sorter on a visible <c>Visuals</c> child on the root, as units do,
    /// drawn on the Entities sorting layer and sorted with no offset at the root, its base, as units sort at their feet;
    /// a sprite, where it has one, pivoted on its base, so the root lies inside it; the root itself draws nothing.
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

            YPositionSorter sorter = visuals.GetComponent<YPositionSorter>();
            Assert.That(sorter, Is.Not.Null, propPrefab.name);
            SerializedObject sorting = new(sorter);
            Assert.That(sorting.FindProperty("sortingOrderOffset").intValue, Is.Zero, propPrefab.name);
            Assert.That(sorting.FindProperty("sortingPoint").objectReferenceValue, Is.EqualTo(propPrefab.transform), $"{propPrefab.name} should sort at its base.");
        }
    }

    /// <summary>
    /// Every prop has a <c>Shadow</c> child on its root, on a layer the camera draws, whose sprite renderer draws on the
    /// ground (the Terrain sorting layer, over the ground and under auras, markers and walls) and so under every unit,
    /// sorted by nothing; its sprite, where it has one, pivoted on the base.
    /// </summary>
    [Test]
    public void Props_DrawTheirShadowOnTheGround_UnderEveryUnit()
    {
        int obstacles = LayerMask.NameToLayer("Obstacles");
        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            Transform shadow = Child(propPrefab, ShadowName);
            Assert.That(shadow.localPosition, Is.EqualTo(Vector3.zero), propPrefab.name);
            Assert.That(shadow.gameObject.layer, Is.Not.EqualTo(obstacles), $"{propPrefab.name}'s shadow must be on a layer the camera draws.");
            Assert.That(shadow.GetComponent<YPositionSorter>(), Is.Null, $"{propPrefab.name}'s shadow is not sorted.");

            SpriteRenderer renderer = shadow.GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null, propPrefab.name);
            Assert.That(renderer.sortingLayerName, Is.EqualTo(ShadowSortingLayer), propPrefab.name);
            Assert.That(renderer.sortingOrder, Is.EqualTo(ShadowSortingOrder), propPrefab.name);
            if (renderer.sprite != null)
            {
                AssertContainsRoot(renderer.sprite.bounds, $"{propPrefab.name}'s shadow should be pivoted on its base.");
            }
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
