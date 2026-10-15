using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prop prefabs for tests that need props the generators can place and the writer can instantiate, now that the
/// project's generators place none: variants of the project's <c>Prop</c> (walk-through) and <c>BlockingProp</c>
/// prefabs, without art, whose footprints have the sizes of the props the generators were built with. Saved as
/// prefab assets in a temporary folder by <see cref="Create"/> and removed with it by <see cref="Delete"/>.
/// </summary>
internal sealed class TestPropPrefabs
{
    public const string Folder = "Assets/Tests/TestPropPrefabs";
    private const string PropPath = "Assets/Prefabs/Props/Prop.prefab";
    private const string BlockingPropPath = "Assets/Prefabs/Props/BlockingProp.prefab";

    private TestPropPrefabs()
    {
        Tree = Blocking("TestTree", new Vector2(1.69f, 0.68f));
        SmallTree = Blocking("TestSmallTree", new Vector2(0.53f, 0.25f));
        Bush = Blocking("TestBush", new Vector2(2.81f, 0.72f));
        Rock = Blocking("TestRock", new Vector2(0.97f, 0.44f));
        Plant = WalkThrough("TestPlant");
        Campfire = Blocking("TestCampfire", new Vector2(1.09f, 0.56f));
        Stool = Blocking("TestStool", new Vector2(0.78f, 0.22f));
        Building = Blocking("TestBuilding", new Vector2(8.75f, 2.53f));
        Fountain = Blocking("TestFountain", new Vector2(1.94f, 1.13f));
        Fence = Blocking("TestFence", new Vector2(1.84f, 0.25f));
        LampPost = Blocking("TestLampPost", new Vector2(0.69f, 0.5f));
        Barrel = Blocking("TestBarrel", new Vector2(0.91f, 0.31f));
        Table = Blocking("TestTable", new Vector2(1.91f, 0.44f));
    }

    public GameObject Tree { get; }
    public GameObject SmallTree { get; }
    public GameObject Bush { get; }
    public GameObject Rock { get; }
    public GameObject Plant { get; }
    public GameObject Campfire { get; }
    public GameObject Stool { get; }
    public GameObject Building { get; }
    public GameObject Fountain { get; }
    public GameObject Fence { get; }
    public GameObject LampPost { get; }
    public GameObject Barrel { get; }
    public GameObject Table { get; }

    /// <summary>
    /// Trees large and small, bushes and rocks, for a scatter layer of blocking props.
    /// </summary>
    public IReadOnlyList<GameObject> BlockingProps => new[] { Tree, Bush, Rock, SmallTree };

    /// <summary>
    /// A rock, a tree and a plant, for a scatter layer of both kinds.
    /// </summary>
    public IReadOnlyList<GameObject> MixedProps => new[] { Rock, Tree, Plant };

    /// <summary>
    /// Plants only, for a scatter layer of walk-through props.
    /// </summary>
    public IReadOnlyList<GameObject> WalkThroughProps => new[] { Plant };

    /// <summary>
    /// A walk-through seat and two stools, for a campsite.
    /// </summary>
    public IReadOnlyList<GameObject> Seats => new[] { Plant, Stool, Stool };

    /// <summary>
    /// Barrels, tables and stools, for a village's yards and square.
    /// </summary>
    public IReadOnlyList<GameObject> TownProps => new[] { Barrel, Table, Stool };

    /// <summary>
    /// Saves the test props in <see cref="Folder"/>, replacing any left there by an earlier run.
    /// </summary>
    public static TestPropPrefabs Create()
    {
        Delete();
        AssetDatabase.CreateFolder("Assets/Tests", "TestPropPrefabs");
        return new TestPropPrefabs();
    }

    /// <summary>
    /// Removes <see cref="Folder"/> and the test props in it.
    /// </summary>
    public static void Delete()
    {
        if (AssetDatabase.IsValidFolder(Folder))
        {
            AssetDatabase.DeleteAsset(Folder);
        }
    }

    /// <summary>
    /// A variant of <c>BlockingProp</c> named <paramref name="name"/> whose footprint is <paramref name="size"/>,
    /// centred on its root and standing on it.
    /// </summary>
    private static GameObject Blocking(string name, Vector2 size)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BlockingPropPath));
        BoxCollider2D box = instance.GetComponent<BoxCollider2D>();
        box.size = size;
        box.offset = new Vector2(0f, size.y * 0.5f);
        return Save(instance, name);
    }

    /// <summary>
    /// A variant of <c>Prop</c> named <paramref name="name"/>, without a footprint.
    /// </summary>
    private static GameObject WalkThrough(string name)
    {
        return Save((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PropPath)), name);
    }

    /// <summary>
    /// Saves <paramref name="instance"/> as the prefab <paramref name="name"/> in <see cref="Folder"/>, destroys the
    /// instance and returns the prefab.
    /// </summary>
    private static GameObject Save(GameObject instance, string name)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, $"{Folder}/{name}.prefab");
        Object.DestroyImmediate(instance);
        return prefab;
    }
}
