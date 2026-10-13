using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="TilePaletteFollower"/>'s choice of palette: an empty ground layer drawn by a terrain renderer
/// finds the project's palette of its material, a tilemap with tiles painted on it the palette holding one of them, a
/// dual-grid layer the palette holding its data tile, and an object that is no tilemap or whose tiles no palette holds
/// finds none. The project's materials, style and palettes are only read.
/// </summary>
public class TilePaletteFollowerTests
{
    private const string PaletteFolder = "Assets/Tiles/Palettes";

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys what the test created.
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
    /// An empty sand layer that a terrain renderer draws with the project's Sand material finds Sand_Palette among the
    /// project's ground palettes, whatever their order.
    /// </summary>
    [Test]
    public void AnEmptyGroundLayer_FindsThePaletteOfItsMaterial()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        Tilemap grass = AddTilemap(grid, "GrassTilemap");
        Tilemap sand = AddTilemap(grid, "SandTilemap");
        TerrainBlendRenderer renderer = grid.gameObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(
            AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset"),
            new TerrainBlendRenderer.Layer(grass, AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Grass.asset")),
            new TerrainBlendRenderer.Layer(sand, AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Sand.asset")));
        List<GameObject> palettes = new();
        foreach (string name in new[] { "Water", "Grass", "Sand", "Mud" })
        {
            palettes.Add(AssetDatabase.LoadAssetAtPath<GameObject>($"{PaletteFolder}/{name}_Palette.prefab"));
        }

        Assert.That(TilePaletteFollower.PaletteFor(sand.gameObject, palettes), Is.SameAs(palettes[2]));
        Assert.That(TilePaletteFollower.PaletteFor(grass.gameObject, palettes), Is.SameAs(palettes[1]));
    }

    /// <summary>
    /// A tilemap no renderer draws finds the palette holding a tile painted on it, and none when no palette holds one.
    /// </summary>
    [Test]
    public void ATilemapWithTiles_FindsThePaletteHoldingOne()
    {
        Tile painted = Track(ScriptableObject.CreateInstance<Tile>());
        Tile other = Track(ScriptableObject.CreateInstance<Tile>());
        GameObject holding = Palette("Holding", painted);
        GameObject notHolding = Palette("Other", other);
        Tilemap tilemap = AddTilemap(Track(new GameObject("Grid")).AddComponent<Grid>(), "Props");
        tilemap.SetTile(new Vector3Int(2, 3, 0), painted);

        Assert.That(TilePaletteFollower.PaletteFor(tilemap.gameObject, new[] { notHolding, holding }), Is.SameAs(holding));
        Assert.That(TilePaletteFollower.PaletteFor(tilemap.gameObject, new[] { notHolding }), Is.Null);
    }

    /// <summary>
    /// An empty dual-grid layer finds the palette holding its module's data tile.
    /// </summary>
    [Test]
    public void ADualGridLayer_FindsThePaletteHoldingItsDataTile()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        DualGridRuleTile ruleTile = Track(ScriptableObject.CreateInstance<DualGridRuleTile>());
        DualGridDataTile dataTile = Track(ruleTile.RefreshDataTile());
        DualGridTilemapModule module = DualGridTilemapModuleEditor.CreateNewDualGridTilemap(grid);
        module.RenderTile = ruleTile;
        GameObject walls = Palette("Walls", dataTile);
        GameObject other = Palette("Other", Track(ScriptableObject.CreateInstance<Tile>()));

        Assert.That(TilePaletteFollower.PaletteFor(module.gameObject, new[] { other, walls }), Is.SameAs(walls));
    }

    /// <summary>
    /// An object that is no tilemap, or none at all, finds no palette.
    /// </summary>
    [Test]
    public void SomethingThatIsNoTilemap_FindsNone()
    {
        GameObject palette = Palette("Any", Track(ScriptableObject.CreateInstance<Tile>()));

        Assert.That(TilePaletteFollower.PaletteFor(Track(new GameObject("Not a tilemap")), new[] { palette }), Is.Null);
        Assert.That(TilePaletteFollower.PaletteFor(null, new[] { palette }), Is.Null);
    }

    /// <summary>
    /// A palette made in memory: a grid with one tilemap holding <paramref name="tile"/>.
    /// </summary>
    private GameObject Palette(string name, TileBase tile)
    {
        Grid grid = Track(new GameObject(name)).AddComponent<Grid>();
        AddTilemap(grid, "Layer1").SetTile(Vector3Int.zero, tile);
        return grid.gameObject;
    }

    /// <summary>
    /// Adds a tilemap named <paramref name="name"/> under <paramref name="grid"/>.
    /// </summary>
    private static Tilemap AddTilemap(Grid grid, string name)
    {
        GameObject child = new(name, typeof(Tilemap));
        child.transform.SetParent(grid.transform, false);
        return child.GetComponent<Tilemap>();
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
