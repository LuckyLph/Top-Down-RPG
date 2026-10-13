using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for <see cref="TerrainTileWriter"/> and the ground tiles it wrote: a swatch is its albedo's bottom-left cell;
/// every terrain material in Assets/Settings/Terrain/Materials has a plain tile (a <see cref="Tile"/>, not a dual-grid
/// data tile) without a collider whose sprite is the material's swatch, one 32 px cell at 32 pixels per unit holding
/// exactly the albedo's bottom-left cell; and each material's palette (Grass_Palette and so on) holds only its
/// layer's tile, while Walls_Palette keeps the walls' data tile. Project assets are only read.
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
    public void EachGroundMaterial_HasAPlainTileShowingItsSwatch()
    {
        List<TerrainMaterial> materials = ProjectMaterials();
        Assert.That(materials.Count, Is.EqualTo(10));

        foreach (TerrainMaterial material in materials)
        {
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(TerrainTileWriter.TilePath(material));
            Assert.That(tile, Is.Not.Null, $"{material.name} has no tile at {TerrainTileWriter.TilePath(material)}.");
            Assert.That(tile.GetType(), Is.EqualTo(typeof(Tile)), $"{tile.name} is not a plain tile.");
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
    /// Each material's palette holds one tile, its layer's plain tile, and the walls palette keeps the walls' dual-grid
    /// data tile.
    /// </summary>
    [Test]
    public void TheGroundPalettes_HoldTheirLayersPlainTile()
    {
        foreach (TerrainMaterial material in ProjectMaterials())
        {
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(TerrainTileWriter.TilePath(material));
            List<TileBase> held = PaletteTiles($"{PaletteFolder}/{material.name}_Palette.prefab");
            Assert.That(held, Is.EqualTo(new[] { tile }), $"{material.name}_Palette");
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
