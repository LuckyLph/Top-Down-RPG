using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for <see cref="TerrainTileWriter"/> and the ground tiles it wrote: a swatch is its albedo's bottom-left cell, and
/// a fixed facing's swatch that cell lying that way under an arrow along the boards; every terrain material in
/// Assets/Settings/Terrain/Materials has a <see cref="TerrainTile"/> keeping a reference to it, without a collider, whose
/// sprite is the material's swatch, and the planks one tile per fixed facing; a material renamed keeps its tile and
/// swatch, found through the tile's reference (Docs/KnownGaps.md, Phase 6); and each material's palette (Grass_Palette
/// and so on) holds only its tiles, while Walls_Palette keeps the walls' data tile. Project assets are only read, but for
/// the rename test's own material, tile and swatch, made and deleted again.
/// </summary>
public class TerrainTileTests
{
    private const string PaletteFolder = "Assets/Tiles/Palettes";

    /// <summary>
    /// The swatch's pixel (x, y) is the albedo's pixel (x, y), for the 32 px cell at the albedo's bottom left.
    /// </summary>
    [Test]
    public void SwatchPixels_AreTheAlbedosBottomLeftCell()
    {
        const int size = 64;
        Color32[] albedo = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                albedo[y * size + x] = new Color32((byte)x, (byte)y, 7, 255);
            }
        }

        Color32[] swatch = TerrainTileWriter.SwatchPixels(size, albedo);

        Assert.That(swatch.Length, Is.EqualTo(TerrainStyle.PixelsPerCell * TerrainStyle.PixelsPerCell));
        for (int y = 0; y < TerrainStyle.PixelsPerCell; y++)
        {
            for (int x = 0; x < TerrainStyle.PixelsPerCell; x++)
            {
                Assert.That(swatch[y * TerrainStyle.PixelsPerCell + x], Is.EqualTo(albedo[y * size + x]), $"pixel ({x}, {y})");
            }
        }
    }

    /// <summary>
    /// An albedo smaller than a cell, or whose pixels do not make the given square, is refused.
    /// </summary>
    [Test]
    public void SwatchPixels_RefuseAnAlbedoThatIsNotASquareOfACellOrMore()
    {
        Assert.Throws<ArgumentException>(() => TerrainTileWriter.SwatchPixels(16, new Color32[16 * 16]));
        Assert.Throws<ArgumentException>(() => TerrainTileWriter.SwatchPixels(64, new Color32[64 * 32]));
    }

    /// <summary>
    /// Each project terrain material has its plain tile, without a collider, whose sprite is the material's swatch:
    /// one 32 px cell at 32 pixels per unit, holding exactly the bottom-left cell of the material's albedo.
    /// </summary>
    [Test]
    public void EachGroundMaterial_HasATileKeepingIt_ShowingItsSwatch()
    {
        List<TerrainMaterial> materials = ProjectMaterials();
        Assert.That(materials.Count, Is.EqualTo(18));

        foreach (TerrainMaterial material in materials)
        {
            TerrainTile tile = TerrainTileWriter.TileOf(material);
            Assert.That(tile, Is.Not.Null, $"{material.name} has no tile.");
            Assert.That(AssetDatabase.GetAssetPath(tile), Is.EqualTo(TerrainTileWriter.TilePath(material)));
            Assert.That(tile.Material == material, Is.True, $"{tile.name} keeps {material.name}.");
            Assert.That(TerrainTileWriter.MaterialOf(tile) == material, Is.True);
            Assert.That(tile.Direction, Is.EqualTo(TerrainTile.Facing.Automatic));
            Assert.That(tile.colliderType, Is.EqualTo(Tile.ColliderType.None));
            Sprite swatch = AssetDatabase.LoadAssetAtPath<Sprite>(TerrainTileWriter.SwatchPath(material));
            Assert.That(tile.sprite == swatch, Is.True, $"{tile.name} shows {material.name}'s swatch.");
            Assert.That(swatch.rect, Is.EqualTo(new Rect(0f, 0f, TerrainStyle.PixelsPerCell, TerrainStyle.PixelsPerCell)));
            Assert.That(swatch.pixelsPerUnit, Is.EqualTo(TerrainStyle.PixelsPerCell));

            Color32[] albedo = Decode(AssetDatabase.GetAssetPath(material.Albedo), out int size);
            Color32[] expected = TerrainTileWriter.SwatchPixels(size, albedo);
            Color32[] written = Decode(TerrainTileWriter.SwatchPath(material), out int swatchSize);
            Assert.That(swatchSize, Is.EqualTo(TerrainStyle.PixelsPerCell));
            Assert.That(written, Is.EqualTo(expected), $"{material.name}'s swatch is not the bottom-left cell of its albedo.");
        }
    }

    /// <summary>
    /// The planks, which turn to their runs, have a tile per fixed facing beside their automatic one, each keeping the
    /// planks and its facing, without a collider, showing its swatch: the cell lying that way under an arrow.
    /// </summary>
    [Test]
    public void ThePlanks_HaveATilePerFacing_ShowingTheBoardsThatWay()
    {
        TerrainMaterial planks = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Planks.asset");
        Assert.That(planks.TurnsToRuns, Is.True);
        Color32[] albedo = Decode(AssetDatabase.GetAssetPath(planks.Albedo), out int size);
        foreach (TerrainTile.Facing facing in new[] { TerrainTile.Facing.EastWest, TerrainTile.Facing.NorthSouth })
        {
            TerrainTile tile = TerrainTileWriter.TileOf(planks, facing);
            Assert.That(tile, Is.Not.Null, $"{facing}");
            Assert.That(AssetDatabase.GetAssetPath(tile), Is.EqualTo(TerrainTileWriter.TilePath(planks, facing)));
            Assert.That(tile.Material == planks && tile.Direction == facing, Is.True, $"{tile.name}");
            Assert.That(tile.colliderType, Is.EqualTo(Tile.ColliderType.None));
            Assert.That(Decode(AssetDatabase.GetAssetPath(tile.sprite), out _), Is.EqualTo(TerrainTileWriter.SwatchPixels(size, albedo, facing)), $"{tile.name}'s swatch");
        }
    }

    /// <summary>
    /// A fixed facing's swatch is the cell as the ground draws it lying that way: east to west the albedo's bottom-left
    /// cell, north to south that cell turned a quarter as the shader turns it (pixel (x, y) the albedo's (y, -x)), each
    /// under a two-headed arrow along the boards, light and outlined, across the middle; the corners keep the cell.
    /// </summary>
    [Test]
    public void AFixedFacingsSwatch_IsTheCellLyingThatWay_UnderAnArrowAlongTheBoards()
    {
        const int size = 64;
        int cell = TerrainStyle.PixelsPerCell;
        Color32[] albedo = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                albedo[y * size + x] = new Color32((byte)x, (byte)y, 7, 255);
            }
        }

        Color32[] eastWest = TerrainTileWriter.SwatchPixels(size, albedo, TerrainTile.Facing.EastWest);
        Color32[] northSouth = TerrainTileWriter.SwatchPixels(size, albedo, TerrainTile.Facing.NorthSouth);
        Assert.That(TerrainTileWriter.SwatchPixels(size, albedo, TerrainTile.Facing.Automatic), Is.EqualTo(TerrainTileWriter.SwatchPixels(size, albedo)));
        foreach (Vector2Int corner in new[] { new Vector2Int(0, 0), new Vector2Int(cell - 1, 0), new Vector2Int(0, cell - 1), new Vector2Int(cell - 1, cell - 1) })
        {
            Assert.That(eastWest[corner.y * cell + corner.x], Is.EqualTo(albedo[corner.y * size + corner.x]), $"east-west corner {corner}");
            Assert.That(northSouth[corner.y * cell + corner.x], Is.EqualTo(albedo[(size - corner.x) % size * size + corner.y]), $"north-south corner {corner}");
        }

        int middle = cell / 2;
        Assert.That(eastWest[middle * cell + middle], Is.Not.EqualTo(albedo[middle * size + middle]), "the arrow's shaft crosses the middle");
        Assert.That(eastWest[middle * cell + 6], Is.EqualTo(eastWest[middle * cell + middle]), "east to west, the shaft runs along x");
        Assert.That(northSouth[6 * cell + middle], Is.EqualTo(northSouth[middle * cell + middle]), "north to south, the shaft runs along y");
        Assert.That(eastWest[6 * cell + middle], Is.EqualTo(TerrainTileWriter.SwatchPixels(size, albedo)[6 * cell + middle]), "east to west, nothing of the arrow above the shaft");
    }

    /// <summary>
    /// A material renamed keeps its tile and swatch: written once (a tile and swatch named after it), renamed, then
    /// written again, it is still found by its tile's reference, the same tile asset, and its swatch is rewritten where
    /// the tile's sprite is, so no tile or swatch is made under the new name. The test's material, albedo, tile and
    /// swatch are made in the project's folders and deleted again.
    /// </summary>
    [Test]
    public void ARenamedMaterial_KeepsItsTileAndSwatch()
    {
        const string folder = "Assets/Tests/Editor/TerrainTileRenameTemp";
        List<string> made = new();
        try
        {
            AssetDatabase.CreateFolder("Assets/Tests/Editor", "TerrainTileRenameTemp");
            string albedoPath = $"{folder}/rename_albedo.png";
            Color32[] pixels = new Color32[TerrainStyle.PixelsPerCell * TerrainStyle.PixelsPerCell];
            Array.Fill(pixels, new Color32(90, 120, 60, 255));
            File.WriteAllBytes(albedoPath, TerrainMaterialWriter.EncodePng(TerrainStyle.PixelsPerCell, pixels));
            AssetDatabase.ImportAsset(albedoPath);
            TerrainMaterial material = ScriptableObject.CreateInstance<TerrainMaterial>();
            SerializedObject settings = new(material);
            settings.FindProperty("albedo").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            settings.ApplyModifiedPropertiesWithoutUndo();
            string materialPath = $"{TerrainTileWriter.MaterialFolder}/TileRenameTemp.asset";
            AssetDatabase.CreateAsset(material, materialPath);
            made.Add(materialPath);

            TerrainTile tile = TerrainTileWriter.Write(material);
            Assert.That(tile, Is.Not.Null);
            string tilePath = AssetDatabase.GetAssetPath(tile);
            string swatchPath = AssetDatabase.GetAssetPath(tile.sprite);
            made.Add(tilePath);
            made.Add(swatchPath);
            Assert.That(tilePath, Is.EqualTo(TerrainTileWriter.TilePath(material)));

            Assert.That(AssetDatabase.RenameAsset(materialPath, "TileRenamedTemp"), Is.Empty);
            made[0] = $"{TerrainTileWriter.MaterialFolder}/TileRenamedTemp.asset";
            TerrainTile again = TerrainTileWriter.Write(material);

            Assert.That(again == tile, Is.True, "the same tile");
            Assert.That(AssetDatabase.GetAssetPath(again), Is.EqualTo(tilePath));
            Assert.That(TerrainTileWriter.TileOf(material) == tile, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(again.sprite), Is.EqualTo(swatchPath), "the same swatch");
            Assert.That(AssetDatabase.LoadMainAssetAtPath(TerrainTileWriter.TilePath(material)), Is.Null, "no tile under the new name");
            Assert.That(File.Exists(TerrainTileWriter.SwatchPath(material)) && TerrainTileWriter.SwatchPath(material) != swatchPath, Is.False, "no swatch under the new name");
        }
        finally
        {
            foreach (string path in made)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.DeleteAsset(folder);
        }
    }

    /// <summary>
    /// Each material's palette holds its tiles: its automatic tile, and for the planks their tiles of the fixed facings
    /// to its right; the walls palette keeps the walls' dual-grid data tile.
    /// </summary>
    [Test]
    public void TheGroundPalettes_HoldTheirLayersTiles()
    {
        foreach (TerrainMaterial material in ProjectMaterials())
        {
            List<TileBase> expected = new();
            foreach (TerrainTile.Facing facing in TerrainTileWriter.FacingsOf(material))
            {
                expected.Add(TerrainTileWriter.TileOf(material, facing));
            }

            List<TileBase> held = PaletteTiles($"{PaletteFolder}/{material.name}_Palette.prefab");
            Assert.That(held, Is.EqualTo(expected), $"{material.name}_Palette");
        }

        List<TileBase> walls = PaletteTiles($"{PaletteFolder}/Walls_Palette.prefab");
        Assert.That(walls.Count, Is.EqualTo(1));
        Assert.That(walls[0], Is.InstanceOf<DualGridDataTile>());
    }

    /// <summary>
    /// The terrain material assets in the project's material folder.
    /// </summary>
    private static List<TerrainMaterial> ProjectMaterials()
    {
        List<TerrainMaterial> materials = new();
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(TerrainMaterial)}", new[] { TerrainTileWriter.MaterialFolder }))
        {
            materials.Add(AssetDatabase.LoadAssetAtPath<TerrainMaterial>(AssetDatabase.GUIDToAssetPath(guid)));
        }

        return materials;
    }

    /// <summary>
    /// The tiles a palette prefab holds, read from its contents loaded apart and unloaded without saving.
    /// </summary>
    private static List<TileBase> PaletteTiles(string path)
    {
        GameObject palette = PrefabUtility.LoadPrefabContents(path);
        try
        {
            List<TileBase> tiles = new();
            foreach (Tilemap tilemap in palette.GetComponentsInChildren<Tilemap>(true))
            {
                foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
                {
                    TileBase tile = tilemap.GetTile(cell);
                    if (tile != null)
                    {
                        tiles.Add(tile);
                    }
                }
            }

            return tiles;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(palette);
        }
    }

    /// <summary>
    /// The pixels of the square PNG at <paramref name="path"/>, as its bytes hold them, rows from the bottom.
    /// </summary>
    private static Color32[] Decode(string path, out int size)
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, path);
            Assert.That(texture.width, Is.EqualTo(texture.height), path);
            size = texture.width;
            return texture.GetPixels32();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }
}
