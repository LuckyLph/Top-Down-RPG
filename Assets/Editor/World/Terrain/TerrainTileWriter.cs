using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Writes the plain tile a ground material's layer is painted with, the tile its palette holds: a <see cref="Tile"/>
/// named Tile_ and the material's name in Assets/Tiles/Ground, without a collider, whose sprite is a swatch of the
/// material, the bottom-left 32 px cell of its albedo, written as a PNG named after the material in
/// Assets/Sprites/Terrain, which <see cref="PixelArtTextureDefaults"/> imports as a 32 PPU point-filtered sprite (sRGB,
/// as the albedo's bytes are sRGB colours) and this writer makes a single sprite. The swatch is only for the Tile
/// Palette: ground layers are data tilemaps without a Tilemap Renderer (the <see cref="TerrainBlendRenderer"/> warns
/// about one that has), so it is never drawn in a scene. Only the materials in Assets/Settings/Terrain/Materials have
/// tiles, since tiles and swatches are named after materials. <see cref="TerrainMaterialWriter.Generate"/> rewrites the
/// swatch of a material that has a tile and points the tile at it again, so the palette keeps showing the material's
/// texture. The same albedo writes byte-identical swatches, and a swatch already holding them is not reimported.
/// </summary>
public static class TerrainTileWriter
{
    public const string TileFolder = "Assets/Tiles/Ground";
    public const string SwatchFolder = "Assets/Sprites/Terrain";
    public const string MaterialFolder = "Assets/Settings/Terrain/Materials";

    /// <summary>
    /// Where <paramref name="material"/>'s tile is: Tile_ and the material's name in <see cref="TileFolder"/>.
    /// </summary>
    public static string TilePath(TerrainMaterial material)
    {
        return $"{TileFolder}/Tile_{material.name}.asset";
    }

    /// <summary>
    /// Where <paramref name="material"/>'s swatch is: its texture files' base name and _swatch.png in
    /// <see cref="SwatchFolder"/>.
    /// </summary>
    public static string SwatchPath(TerrainMaterial material)
    {
        return $"{SwatchFolder}/{TerrainMaterialWriter.FileBaseName(material)}_swatch.png";
    }

    /// <summary>
    /// The swatch's pixels: the bottom-left cell (<see cref="TerrainStyle.PixelsPerCell"/> px square) of a
    /// <paramref name="size"/> px square albedo, rows from the bottom as in <see cref="Texture2D.GetPixels32()"/>.
    /// </summary>
    public static Color32[] SwatchPixels(int size, Color32[] albedo)
    {
        int cell = TerrainStyle.PixelsPerCell;
        if (size < cell || albedo.Length != size * size)
        {
            throw new ArgumentException($"An albedo of {albedo.Length} pixels is not {size} px square of at least {cell} px.", nameof(albedo));
        }

        Color32[] swatch = new Color32[cell * cell];
        for (int y = 0; y < cell; y++)
        {
            Array.Copy(albedo, y * size, swatch, y * cell, cell);
        }

        return swatch;
    }

    /// <summary>
    /// Writes <paramref name="material"/>'s swatch from its albedo PNG, then creates its tile or points the existing
    /// one at the swatch without a collider, and returns the tile. Logs an error and writes nothing when the material
    /// is not in <see cref="MaterialFolder"/> (tiles and swatches are named after materials, whose names are unique
    /// only there), when its albedo is not a square PNG of at least a cell, or when something other than a plain tile
    /// is at the tile's path.
    /// </summary>
    public static Tile Write(TerrainMaterial material)
    {
        if (!IsInMaterialFolder(material))
        {
            Debug.LogError($"Terrain material {material.name} is not in {MaterialFolder}, so it has no ground tile.", material);
            return null;
        }

        string albedoPath = material.Albedo != null ? AssetDatabase.GetAssetPath(material.Albedo) : null;
        if (string.IsNullOrEmpty(albedoPath) || !albedoPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"Terrain material {material.name} has no albedo PNG to make its swatch from.", material);
            return null;
        }

        Object atTilePath = AssetDatabase.LoadMainAssetAtPath(TilePath(material));
        if (atTilePath != null && atTilePath.GetType() != typeof(Tile))
        {
            Debug.LogError($"{TilePath(material)} is a {atTilePath.GetType().Name}, not {material.name}'s plain tile; move it before writing the tile.", atTilePath);
            return null;
        }

        Texture2D decoded = new(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!decoded.LoadImage(File.ReadAllBytes(albedoPath)) || decoded.width != decoded.height || decoded.width < TerrainStyle.PixelsPerCell)
            {
                Debug.LogError($"{albedoPath} of terrain material {material.name} is not a square PNG of at least {TerrainStyle.PixelsPerCell} px.", material);
                return null;
            }

            WriteSwatch(material, decoded.width, decoded.GetPixels32());
        }
        finally
        {
            Object.DestroyImmediate(decoded);
        }

        return WriteTile(material);
    }

    /// <summary>
    /// Rewrites <paramref name="material"/>'s swatch from the albedo in <paramref name="textures"/> and points its tile
    /// at it again, when the material is in <see cref="MaterialFolder"/> and has a plain tile; does nothing otherwise.
    /// </summary>
    public static void RefreshSwatch(TerrainMaterial material, TerrainTextures textures)
    {
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(TilePath(material));
        if (IsInMaterialFolder(material) && tile != null && tile.GetType() == typeof(Tile))
        {
            WriteSwatch(material, textures.Size, textures.Albedo);
            WriteTile(material);
        }
    }

    /// <summary>
    /// Whether <paramref name="material"/> is an asset in <see cref="MaterialFolder"/>.
    /// </summary>
    private static bool IsInMaterialFolder(TerrainMaterial material)
    {
        return AssetDatabase.GetAssetPath(material).StartsWith(MaterialFolder + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes the tiles and swatches of every terrain material in <see cref="MaterialFolder"/>.
    /// </summary>
    [MenuItem("Tools/TopDownRPG/World/Terrain/Write Ground Tiles")]
    private static void WriteProjectTiles()
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(TerrainMaterial)}", new[] { MaterialFolder }))
        {
            TerrainMaterial material = AssetDatabase.LoadAssetAtPath<TerrainMaterial>(AssetDatabase.GUIDToAssetPath(guid));
            Tile tile = material != null ? Write(material) : null;
            if (tile != null)
            {
                Debug.Log($"Wrote {AssetDatabase.GetAssetPath(tile)} with the swatch {SwatchPath(material)}.", tile);
            }
        }
    }

    /// <summary>
    /// Writes the swatch PNG of <paramref name="material"/> from a <paramref name="size"/> px square albedo and
    /// imports it as a single sprite; leaves an imported file that already holds those bytes alone.
    /// </summary>
    private static void WriteSwatch(TerrainMaterial material, int size, Color32[] albedo)
    {
        TerrainMaterialWriter.EnsureFolder(SwatchFolder);
        string path = SwatchPath(material);
        byte[] png = TerrainMaterialWriter.EncodePng(TerrainStyle.PixelsPerCell, SwatchPixels(size, albedo));
        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png) || AssetImporter.GetAtPath(path) == null)
        {
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// Creates <paramref name="material"/>'s tile with its swatch and no collider, or sets those on the existing tile
    /// through its serialized data, saving it only when something changed.
    /// </summary>
    private static Tile WriteTile(TerrainMaterial material)
    {
        Sprite swatch = AssetDatabase.LoadAssetAtPath<Sprite>(SwatchPath(material));
        string path = TilePath(material);
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (tile == null)
        {
            TerrainMaterialWriter.EnsureFolder(TileFolder);
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = swatch;
            tile.colliderType = Tile.ColliderType.None;
            AssetDatabase.CreateAsset(tile, path);
            return tile;
        }

        SerializedObject serialized = new(tile);
        serialized.FindProperty("m_Sprite").objectReferenceValue = swatch;
        serialized.FindProperty("m_ColliderType").intValue = (int)Tile.ColliderType.None;
        if (serialized.ApplyModifiedPropertiesWithoutUndo())
        {
            AssetDatabase.SaveAssetIfDirty(tile);
        }

        return tile;
    }
}
