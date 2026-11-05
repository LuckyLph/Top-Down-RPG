using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Writes the plain tile a ground material's layer is painted with, the tile its palette holds: a
/// <see cref="TerrainTile"/> that keeps a reference to its material, created as Tile_ and the material's name in
/// Assets/Tiles/Ground, without a collider, whose sprite is a swatch of the material, the bottom-left 32 px cell of its
/// albedo, written as a PNG named after the material in Assets/Sprites/Terrain, which
/// <see cref="PixelArtTextureDefaults"/> imports as a 32 PPU point-filtered sprite (sRGB, as the albedo's bytes are sRGB
/// colours) and this writer makes a single sprite. A material's tile is found by that reference
/// (<see cref="TileOf"/>), and its swatch through the tile's sprite, so a material, tile or swatch renamed or moved keeps
/// them (Docs/KnownGaps.md, Phase 6); only a material with no tile yet gets one at the paths named after it, and a plain
/// <see cref="Tile"/> found there is turned into its <see cref="TerrainTile"/> in place, its GUID kept, so the scenes and
/// palettes painted with it keep it. A material that turns to its runs (the planks) also gets a tile for each fixed
/// way it can lie (<see cref="TerrainTile.Facing"/>), Tile_ and its name, then _EastWest or _NorthSouth, with a swatch
/// of its texture lying that way and a two-headed arrow along its boards, and its palette (the material's name and
/// _Palette in Assets/Tiles/Palettes, when there is one) holds them beside its automatic tile. The swatch is only for
/// the Tile Palette: ground layers are data tilemaps without a Tilemap Renderer (the <see cref="TerrainBlendRenderer"/>
/// warns about one that has), so it is never drawn in a scene. Write Ground Tiles writes the materials in
/// Assets/Settings/Terrain/Materials. <see cref="TerrainMaterialWriter.Generate"/> rewrites the swatches of a material
/// that has a tile and points its tiles at them again, so the palette keeps showing the material's texture. The same
/// albedo writes byte-identical swatches, and a swatch already holding them is not reimported.
/// </summary>
public static class TerrainTileWriter
{
    public const string TileFolder = "Assets/Tiles/Ground";
    public const string SwatchFolder = "Assets/Sprites/Terrain";
    public const string MaterialFolder = "Assets/Settings/Terrain/Materials";
    public const string PaletteFolder = "Assets/Tiles/Palettes";
    private const string TileScriptPath = "Assets/Scripts/World/Terrain/TerrainTile.cs";
    private static readonly TerrainTile.Facing[] FixedFacings = { TerrainTile.Facing.EastWest, TerrainTile.Facing.NorthSouth };
    private static readonly Color32 ArrowColour = new(245, 236, 210, 255);
    private static readonly Color32 ArrowOutline = new(42, 31, 22, 255);

    /// <summary>
    /// Where a new tile of <paramref name="material"/> lying <paramref name="facing"/> is written: Tile_ and the
    /// material's name in <see cref="TileFolder"/>, then _EastWest or _NorthSouth for a fixed facing.
    /// </summary>
    public static string TilePath(TerrainMaterial material, TerrainTile.Facing facing = TerrainTile.Facing.Automatic)
    {
        return $"{TileFolder}/Tile_{material.name}{FacingSuffix(facing)}.asset";
    }

    /// <summary>
    /// The tile of <paramref name="material"/> lying <paramref name="facing"/>: the first <see cref="TerrainTile"/> of the
    /// project, by path, whose material and facing they are, wherever it lies and however it is named; null when there
    /// is none.
    /// </summary>
    public static TerrainTile TileOf(TerrainMaterial material, TerrainTile.Facing facing = TerrainTile.Facing.Automatic)
    {
        if (material == null)
        {
            return null;
        }

        string[] paths = AssetDatabase.FindAssets($"t:{nameof(TerrainTile)}").Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        foreach (string path in paths)
        {
            TerrainTile tile = AssetDatabase.LoadAssetAtPath<TerrainTile>(path);
            if (tile != null && tile.Material == material && tile.Direction == facing)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>
    /// The ground material <paramref name="tile"/> paints (<see cref="TerrainTile.Material"/>), or null for any other
    /// tile.
    /// </summary>
    public static TerrainMaterial MaterialOf(TileBase tile)
    {
        return tile is TerrainTile terrainTile ? terrainTile.Material : null;
    }

    /// <summary>
    /// Where a new swatch of <paramref name="material"/> lying <paramref name="facing"/> is written: its texture files'
    /// base name, then _eastwest or _northsouth for a fixed facing, and _swatch.png in <see cref="SwatchFolder"/>.
    /// </summary>
    public static string SwatchPath(TerrainMaterial material, TerrainTile.Facing facing = TerrainTile.Facing.Automatic)
    {
        return $"{SwatchFolder}/{TerrainMaterialWriter.FileBaseName(material)}{FacingSuffix(facing).ToLowerInvariant()}_swatch.png";
    }

    /// <summary>
    /// Where <paramref name="material"/>'s palette is: its name and _Palette in <see cref="PaletteFolder"/>.
    /// </summary>
    public static string PalettePath(TerrainMaterial material)
    {
        return $"{PaletteFolder}/{material.name}_Palette.prefab";
    }

    /// <summary>
    /// The facings a tile of <paramref name="material"/> is written in: automatic, and for a material that turns to its
    /// runs east to west and north to south as well.
    /// </summary>
    public static TerrainTile.Facing[] FacingsOf(TerrainMaterial material)
    {
        return material != null && material.TurnsToRuns
            ? new[] { TerrainTile.Facing.Automatic, TerrainTile.Facing.EastWest, TerrainTile.Facing.NorthSouth }
            : new[] { TerrainTile.Facing.Automatic };
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
    /// The swatch of a material lying <paramref name="facing"/>: for automatic the albedo's bottom-left cell
    /// (<see cref="SwatchPixels(int, Color32[])"/>); for a fixed facing the cell as the ground draws it lying that way,
    /// turned a quarter for north to south as the shader turns it (the texture's x running up the world and its top
    /// towards the left), with a two-headed arrow along the boards (<see cref="DrawArrow"/>).
    /// </summary>
    public static Color32[] SwatchPixels(int size, Color32[] albedo, TerrainTile.Facing facing)
    {
        Color32[] swatch = SwatchPixels(size, albedo);
        if (facing == TerrainTile.Facing.Automatic)
        {
            return swatch;
        }

        int cell = TerrainStyle.PixelsPerCell;
        if (facing == TerrainTile.Facing.NorthSouth)
        {
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    swatch[y * cell + x] = albedo[(size - x) % size * size + y];
                }
            }
        }

        DrawArrow(swatch, cell, facing == TerrainTile.Facing.EastWest);
        return swatch;
    }

    /// <summary>
    /// Draws a two-headed arrow across the middle of a <paramref name="cell"/> px square of pixels, rows from the bottom,
    /// along x when <paramref name="alongX"/>, else along y: a light shaft two pixels thick with a head at each end five
    /// pixels long, outlined one pixel wide in a dark brown.
    /// </summary>
    public static void DrawArrow(Color32[] pixels, int cell, bool alongX)
    {
        bool[] core = new bool[cell * cell];
        int middle = cell / 2;
        for (int along = 4; along < cell - 4; along++)
        {
            int fromEnd = Math.Min(along - 4, cell - 5 - along);
            int half = fromEnd < 5 ? fromEnd + 1 : 1;
            for (int across = middle - half; across < middle + half; across++)
            {
                core[alongX ? across * cell + along : along * cell + across] = true;
            }
        }

        for (int y = 0; y < cell; y++)
        {
            for (int x = 0; x < cell; x++)
            {
                if (core[y * cell + x])
                {
                    pixels[y * cell + x] = ArrowColour;
                    continue;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < cell && ny < cell && core[ny * cell + nx])
                        {
                            pixels[y * cell + x] = ArrowOutline;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="material"/>'s swatches from its albedo PNG, then its tiles (<see cref="FacingsOf"/>): each
    /// found by reference (<see cref="TileOf"/>) is pointed at its swatch without a collider, and a missing one is
    /// created, or turned from a plain tile at its path into a <see cref="TerrainTile"/>; then the material's palette
    /// holds its fixed tiles beside its automatic one. Returns the automatic tile. Logs an error and writes nothing when
    /// the material is not in <see cref="MaterialFolder"/> (new tiles and swatches are named after materials, whose
    /// names are unique only there), when its albedo is not a square PNG of at least a cell, or when something other
    /// than a tile is at a missing tile's path.
    /// </summary>
    public static TerrainTile Write(TerrainMaterial material)
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

        foreach (TerrainTile.Facing facing in FacingsOf(material))
        {
            Object atTilePath = TileOf(material, facing) == null ? AssetDatabase.LoadMainAssetAtPath(TilePath(material, facing)) : null;
            if (atTilePath != null && atTilePath is not Tile)
            {
                Debug.LogError($"{TilePath(material, facing)} is a {atTilePath.GetType().Name}, not {material.name}'s tile; move it before writing the tile.", atTilePath);
                return null;
            }
        }

        Texture2D decoded = new(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!decoded.LoadImage(File.ReadAllBytes(albedoPath)) || decoded.width != decoded.height || decoded.width < TerrainStyle.PixelsPerCell)
            {
                Debug.LogError($"{albedoPath} of terrain material {material.name} is not a square PNG of at least {TerrainStyle.PixelsPerCell} px.", material);
                return null;
            }

            return WriteAll(material, decoded.width, decoded.GetPixels32());
        }
        finally
        {
            Object.DestroyImmediate(decoded);
        }
    }

    /// <summary>
    /// Rewrites the swatches of <paramref name="material"/>'s tiles from the albedo in <paramref name="textures"/> and
    /// points its tiles at them again, when the material has a tile (found by reference, <see cref="TileOf"/>); does
    /// nothing otherwise, so a material without a tile gets none here.
    /// </summary>
    public static void RefreshSwatch(TerrainMaterial material, TerrainTextures textures)
    {
        if (TileOf(material) != null)
        {
            WriteAll(material, textures.Size, textures.Albedo);
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
            TerrainTile tile = material != null ? Write(material) : null;
            if (tile != null)
            {
                Debug.Log($"Wrote {AssetDatabase.GetAssetPath(tile)} with the swatch {AssetDatabase.GetAssetPath(tile.sprite)}.", tile);
            }
        }
    }

    /// <summary>
    /// Writes each of <paramref name="material"/>'s swatches from a <paramref name="size"/> px square albedo, at the
    /// path its tile's sprite has or else at <see cref="SwatchPath"/>, and its tile pointing at it, then, for a material
    /// that turns to its runs, its palette's fixed tiles; returns the automatic tile.
    /// </summary>
    private static TerrainTile WriteAll(TerrainMaterial material, int size, Color32[] albedo)
    {
        TerrainTile automatic = null;
        foreach (TerrainTile.Facing facing in FacingsOf(material))
        {
            TerrainTile existing = TileOf(material, facing);
            string swatchPath = existing != null && existing.sprite != null ? AssetDatabase.GetAssetPath(existing.sprite) : SwatchPath(material, facing);
            WriteSwatch(swatchPath, SwatchPixels(size, albedo, facing));
            TerrainTile tile = WriteTile(material, facing, existing, AssetDatabase.LoadAssetAtPath<Sprite>(swatchPath));
            automatic = facing == TerrainTile.Facing.Automatic ? tile : automatic;
        }

        if (material.TurnsToRuns)
        {
            WritePalette(material);
        }

        return automatic;
    }

    /// <summary>
    /// Writes <paramref name="pixels"/> as the swatch PNG at <paramref name="path"/> and imports it as a single sprite;
    /// leaves an imported file that already holds those bytes alone.
    /// </summary>
    private static void WriteSwatch(string path, Color32[] pixels)
    {
        TerrainMaterialWriter.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
        byte[] png = TerrainMaterialWriter.EncodePng(TerrainStyle.PixelsPerCell, pixels);
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
    /// Sets <paramref name="existing"/>, or the plain tile at the tile's path turned into a <see cref="TerrainTile"/>
    /// (<see cref="Convert"/>), or a new tile at that path, to paint <paramref name="material"/> lying
    /// <paramref name="facing"/> with <paramref name="swatch"/> and no collider, through its serialized data, saving it
    /// only when something changed.
    /// </summary>
    private static TerrainTile WriteTile(TerrainMaterial material, TerrainTile.Facing facing, TerrainTile existing, Sprite swatch)
    {
        TerrainTile tile = existing;
        string path = TilePath(material, facing);
        if (tile == null && AssetDatabase.LoadMainAssetAtPath(path) is Tile plain)
        {
            tile = plain as TerrainTile ?? Convert(plain);
        }

        if (tile == null)
        {
            TerrainMaterialWriter.EnsureFolder(TileFolder);
            tile = ScriptableObject.CreateInstance<TerrainTile>();
            AssetDatabase.CreateAsset(tile, path);
        }

        SerializedObject serialized = new(tile);
        serialized.FindProperty("m_Sprite").objectReferenceValue = swatch;
        serialized.FindProperty("m_ColliderType").intValue = (int)Tile.ColliderType.None;
        serialized.FindProperty("material").objectReferenceValue = material;
        serialized.FindProperty("facing").enumValueIndex = (int)facing;
        if (serialized.ApplyModifiedPropertiesWithoutUndo())
        {
            AssetDatabase.SaveAssetIfDirty(tile);
        }

        return tile;
    }

    /// <summary>
    /// Turns the plain <paramref name="tile"/> into a <see cref="TerrainTile"/> in place, as the inspector's script field
    /// does: the asset keeps its GUID, name and tile settings, so everything painted with it keeps it. Returns the
    /// converted tile, or null after logging why when the editor would not turn it.
    /// </summary>
    private static TerrainTile Convert(Tile tile)
    {
        string path = AssetDatabase.GetAssetPath(tile);
        SerializedObject serialized = new(tile);
        serialized.FindProperty("m_Script").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MonoScript>(TileScriptPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (AssetDatabase.LoadMainAssetAtPath(path) is not TerrainTile converted)
        {
            Debug.LogError($"{path} could not be turned into a {nameof(TerrainTile)}.", AssetDatabase.LoadMainAssetAtPath(path));
            return null;
        }

        EditorUtility.SetDirty(converted);
        AssetDatabase.SaveAssetIfDirty(converted);
        return converted;
    }

    /// <summary>
    /// Has <paramref name="material"/>'s palette (<see cref="PalettePath"/>), when it has one, hold its tiles of the
    /// fixed facings in the cells to the right of its automatic tile; saves the palette only when a cell changed.
    /// </summary>
    private static void WritePalette(TerrainMaterial material)
    {
        string path = PalettePath(material);
        if (AssetDatabase.LoadMainAssetAtPath(path) == null)
        {
            return;
        }

        GameObject palette = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Tilemap tilemap = palette.GetComponentInChildren<Tilemap>();
            TerrainTile automatic = TileOf(material);
            Vector3Int origin = Vector3Int.zero;
            foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
            {
                if (automatic != null && tilemap.GetTile(cell) == automatic)
                {
                    origin = cell;
                    break;
                }
            }

            bool changed = false;
            for (int i = 0; i < FixedFacings.Length; i++)
            {
                Vector3Int cell = origin + new Vector3Int(i + 1, 0, 0);
                TerrainTile tile = TileOf(material, FixedFacings[i]);
                if (tile != null && tilemap.GetTile(cell) != tile)
                {
                    tilemap.SetTile(cell, tile);
                    changed = true;
                }
            }

            if (changed)
            {
                tilemap.RefreshAllTiles();
                PrefabUtility.SaveAsPrefabAsset(palette, path);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(palette);
        }
    }

    /// <summary>
    /// The suffix of a tile's name for <paramref name="facing"/>: none for automatic, else _ and the facing's name.
    /// </summary>
    private static string FacingSuffix(TerrainTile.Facing facing)
    {
        return facing == TerrainTile.Facing.Automatic ? string.Empty : "_" + facing;
    }
}
