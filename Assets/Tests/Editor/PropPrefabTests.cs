using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the prop prefabs under <c>Assets/Prefabs/Props</c>: one variant of <c>Prop</c> or <c>BlockingProp</c>
/// per sliced prop sprite, laid out like the player and mob prefabs (the root at the base, a <c>Visuals</c> child
/// holding the sprite and its sorter), the footprints of blocking props, and how a placed prop blocks navigation.
/// </summary>
public class PropPrefabTests
{
    private const string PropsFolder = "Assets/Prefabs/Props";
    private const string PropPath = PropsFolder + "/Prop.prefab";
    private const string BlockingPropPath = PropsFolder + "/BlockingProp.prefab";
    private const string TreePath = PropsFolder + "/Trees/Tree_Pine.prefab";
    private const string GrassPath = PropsFolder + "/Plants/Grass_Tall.prefab";
    private const string VisualsName = "Visuals";
    private const float VisualsHeight = 0.5f;
    private const float OnePixel = 1f / 32f;

    private static readonly string[] PropSheets =
    {
        "Assets/Sprites/Props/magecity.png",
        "Assets/Sprites/Props/terrain_atlas.png",
        "Assets/Sprites/Props/mountain_landscape.png",
        "Assets/Sprites/Props/wood_tileset.png",
    };

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
    /// Every sprite sliced from the prop sheets has exactly one prefab named after it, and every prop prefab is a
    /// variant of <c>Prop</c> or <c>BlockingProp</c>, which is itself a variant of <c>Prop</c>.
    /// </summary>
    [Test]
    public void EveryPropSprite_HasOnePrefab_ThatIsAVariantOfABaseProp()
    {
        GameObject prop = AssetDatabase.LoadAssetAtPath<GameObject>(PropPath);
        GameObject blocking = AssetDatabase.LoadAssetAtPath<GameObject>(BlockingPropPath);
        Assert.That(prop, Is.Not.Null);
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(blocking), Is.SameAs(prop));

        string[] spriteNames = PropSheets
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath)
            .OfType<Sprite>()
            .Select(sprite => sprite.name)
            .OrderBy(name => name)
            .ToArray();
        List<GameObject> props = LoadPropPrefabs();

        Assert.That(props.Select(p => p.name).OrderBy(name => name), Is.EqualTo(spriteNames));
        foreach (GameObject propPrefab in props)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(propPrefab);
            Assert.That(source == prop || source == blocking, Is.True, $"{propPrefab.name} should be a variant of Prop or BlockingProp.");
            Assert.That(Visuals(propPrefab).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo(propPrefab.name));
        }
    }

    /// <summary>
    /// Like the player, every prop keeps its sprite and sorter on a visible <c>Visuals</c> child half a unit above the
    /// root (it may be nudged sideways to centre the art on the footprint), with the sprite pivoted so its base meets
    /// the root, drawn on the Entities sorting layer and sorted with no offset; the root itself draws nothing.
    /// </summary>
    [Test]
    public void Props_KeepTheirSpriteOnAVisualsChild_HalfAUnitAboveTheirBase()
    {
        int obstacles = LayerMask.NameToLayer("Obstacles");
        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            Assert.That(propPrefab.GetComponent<Renderer>(), Is.Null, $"{propPrefab.name}'s root draws nothing.");
            Transform visuals = Visuals(propPrefab);
            Assert.That(visuals.localPosition.y, Is.EqualTo(VisualsHeight), propPrefab.name);
            Assert.That(visuals.localPosition.z, Is.Zero, propPrefab.name);
            Assert.That(visuals.gameObject.layer, Is.Not.EqualTo(obstacles), $"{propPrefab.name}'s visuals must be on a layer the camera draws.");

            SpriteRenderer renderer = visuals.GetComponent<SpriteRenderer>();
            Assert.That(renderer.sortingLayerName, Is.EqualTo("Entities"), propPrefab.name);
            Assert.That(VisualsHeight + renderer.sprite.bounds.min.y, Is.EqualTo(0f).Within(0.001f), $"{propPrefab.name}'s sprite should stand on the root.");

            YPositionSorter sorter = visuals.GetComponent<YPositionSorter>();
            Assert.That(sorter, Is.Not.Null, propPrefab.name);
            Assert.That(new SerializedObject(sorter).FindProperty("sortingOrderOffset").intValue, Is.Zero, propPrefab.name);
        }
    }

    /// <summary>
    /// A blocking prop's root is on the Obstacles layer and holds its only collider, a solid box starting within a
    /// pixel of the root and lying within the sprite's width as drawn, like the feet collider on the player's root (it
    /// may be off centre, to follow the art); a walk-through prop has no collider at all.
    /// </summary>
    [Test]
    public void BlockingProps_HaveAFootprintAtTheirBase_AndOthersNoCollider()
    {
        GameObject blocking = AssetDatabase.LoadAssetAtPath<GameObject>(BlockingPropPath);
        int obstacles = LayerMask.NameToLayer("Obstacles");
        foreach (GameObject propPrefab in LoadPropPrefabs())
        {
            Collider2D[] colliders = propPrefab.GetComponentsInChildren<Collider2D>(true);
            if (PrefabUtility.GetCorrespondingObjectFromSource(propPrefab) != blocking)
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
            Assert.That(footprint.offset.y - footprint.size.y * 0.5f, Is.EqualTo(0f).Within(OnePixel), $"{propPrefab.name}'s footprint should start at its base.");

            Transform visuals = Visuals(propPrefab);
            Bounds sprite = visuals.GetComponent<SpriteRenderer>().sprite.bounds;
            float spriteLeft = visuals.localPosition.x + sprite.min.x;
            float spriteRight = visuals.localPosition.x + sprite.max.x;
            Assert.That(footprint.offset.x - footprint.size.x * 0.5f, Is.GreaterThanOrEqualTo(spriteLeft - OnePixel), $"{propPrefab.name}'s footprint should lie under its sprite.");
            Assert.That(footprint.offset.x + footprint.size.x * 0.5f, Is.LessThanOrEqualTo(spriteRight + OnePixel), $"{propPrefab.name}'s footprint should lie under its sprite.");
        }
    }

    /// <summary>
    /// A tree placed in a cell blocks that cell, and only that one, on a grid whose obstacle layers include Obstacles,
    /// while tall grass placed in another cell blocks nothing.
    /// </summary>
    [Test]
    public void APlacedTree_BlocksItsCell_AndGrassBlocksNothing()
    {
        NavigationGrid2D grid = CreateGround(5);
        Place(TreePath, new Vector2(2.5f, 2.05f));
        Place(GrassPath, new Vector2(0.5f, 0.05f));
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
    /// Every prefab under the props folder except the two base prefabs.
    /// </summary>
    private static List<GameObject> LoadPropPrefabs()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { PropsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path != PropPath && path != BlockingPropPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .ToList();
    }

    /// <summary>
    /// The <c>Visuals</c> child of <paramref name="propPrefab"/>, failing the test when it has none.
    /// </summary>
    private static Transform Visuals(GameObject propPrefab)
    {
        Transform visuals = propPrefab.transform.Find(VisualsName);
        Assert.That(visuals, Is.Not.Null, $"{propPrefab.name} should have a {VisualsName} child.");
        return visuals;
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
    /// Places an instance of the prefab at <paramref name="path"/> with its base at <paramref name="position"/>.
    /// </summary>
    private void Place(string path, Vector2 position)
    {
        GameObject instance = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)));
        instance.transform.position = position;
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
