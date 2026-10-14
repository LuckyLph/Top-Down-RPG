using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="TilePaletteFollower"/>'s choice of palette: an empty ground layer drawn by a terrain renderer
/// finds the project's palette of its material (every material of the style's layer order has one), a tilemap with
/// tiles painted on it the palette holding one of them, a
/// dual-grid layer the palette holding its data tile, and an object that is no tilemap or whose tiles no palette holds
/// finds none; and the Terrain Brush's palette check (<see cref="TerrainBrushEditor.PaletteProblem"/>) for an area that
/// draws only some of the ground materials. The project's materials, style, tiles and palettes are only read.
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
    /// For every material of the project style's layer order (dirt, dry grass and plaza among them), an empty layer a
    /// terrain renderer draws with it finds that material's own palette among all the project's palettes.
    /// </summary>
    [Test]
    public void EveryGroundMaterial_HasAPaletteAnEmptyLayerFinds()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        List<GameObject> palettes = new();
        foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { PaletteFolder }))
        {
            palettes.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
        }

        Assert.That(style.LayerOrder.Count, Is.EqualTo(13));
        foreach (TerrainMaterial material in style.LayerOrder)
        {
            Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
            Tilemap layer = AddTilemap(grid, $"{material.name}Tilemap");
            grid.gameObject.AddComponent<TerrainBlendRenderer>().Configure(style, new TerrainBlendRenderer.Layer(layer, material));

            GameObject palette = TilePaletteFollower.PaletteFor(layer.gameObject, palettes);

            Assert.That(palette, Is.Not.Null, material.name);
            Assert.That(palette.name, Is.EqualTo($"{material.name}_Palette"), material.name);
        }
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
    /// In an area drawing only grass and sand, the Terrain Brush's palette check says nothing for grass's tile on the
    /// grass layer or for a tile of no ground material, names the sand layer for sand's tile picked on the grass layer,
    /// and says the area has no water layer for water's tile; on a tilemap no renderer draws it says nothing.
    /// </summary>
    [Test]
    public void ThePaletteCheck_NamesTheLayerOfAPickedGroundTile_OrSaysTheAreaHasNone()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        Tilemap grass = AddTilemap(grid, "GrassTilemap");
        Tilemap sand = AddTilemap(grid, "SandTilemap");
        Tilemap other = AddTilemap(grid, "Other");
        TerrainBlendRenderer renderer = grid.gameObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(
            AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset"),
            new TerrainBlendRenderer.Layer(grass, AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Grass.asset")),
            new TerrainBlendRenderer.Layer(sand, AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Sand.asset")));
        TileBase grassTile = AssetDatabase.LoadAssetAtPath<TileBase>($"{TerrainTileWriter.TileFolder}/Tile_Grass.asset");
        TileBase sandTile = AssetDatabase.LoadAssetAtPath<TileBase>($"{TerrainTileWriter.TileFolder}/Tile_Sand.asset");
        TileBase waterTile = AssetDatabase.LoadAssetAtPath<TileBase>($"{TerrainTileWriter.TileFolder}/Tile_Water.asset");
        TileBase plain = Track(ScriptableObject.CreateInstance<Tile>());

        Assert.That(TerrainBrushEditor.PaletteProblem(grass.gameObject, new[] { grassTile, plain, null }), Is.Null);
        Assert.That(TerrainBrushEditor.PaletteProblem(sand.gameObject, new[] { sandTile }), Is.Null);
        Assert.That(TerrainBrushEditor.PaletteProblem(grass.gameObject, new[] { sandTile }), Does.Contain("Pick SandTilemap as the paint target"));
        Assert.That(TerrainBrushEditor.PaletteProblem(grass.gameObject, new[] { waterTile }), Does.StartWith("Water is not one of this area's ground layers"));
        Assert.That(TerrainBrushEditor.PaletteProblem(other.gameObject, new[] { waterTile }), Is.Null);
        Assert.That(TerrainBrushEditor.PaletteProblem(null, new[] { waterTile }), Is.Null);
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
