using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Writes what a <see cref="WallSheetRecipe"/> generates as its sheet: the PNG's bytes, then the sheet's import
/// settings (a point-filtered, uncompressed sprite sheet without mipmaps at one piece per unit) and its 16 slices, each
/// named after the file and its piece (walls_0 to walls_15) at its <see cref="WallSheetRecipe.PieceRect"/>. Slices that
/// already exist keep their sprite IDs, so the rule tile and palette references to them survive a change of size; a
/// missing one is added. The pixels depend only on the recipe and its seed and the encoding only on the pixels, so a
/// rerun writes the same bytes, and an unchanged sheet is not reimported.
/// </summary>
public static class WallSheetWriter
{
    /// <summary>
    /// Generates <paramref name="recipe"/>'s sheet for its seed, writes it over the recipe's sheet texture and slices
    /// it. Logs an error and writes nothing when the recipe's settings cannot make a sheet, its rule tile cannot say
    /// what each piece fills, or it has no PNG sheet.
    /// </summary>
    public static bool Generate(WallSheetRecipe recipe)
    {
        int[] masks = WallSheetRecipe.PieceMasks(recipe.RuleTile, out string problem);
        if (masks == null || !recipe.CanGenerate(out problem))
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            return false;
        }

        string path = recipe.Sheet != null ? AssetDatabase.GetAssetPath(recipe.Sheet) : null;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"{recipe.name} has no PNG sheet to write.", recipe);
            return false;
        }

        byte[] png = TerrainMaterialWriter.EncodePng(recipe.SheetSize, recipe.Generate(recipe.Seed, masks));
        bool written = !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png);
        if (written)
        {
            File.WriteAllBytes(path, png);
        }

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool sliced = ApplyImportSettings(importer, recipe.PieceSize);
        sliced |= Slice(importer, Path.GetFileNameWithoutExtension(path), recipe.PieceSize);
        if (written || sliced)
        {
            importer.SaveAndReimport();
        }

        return true;
    }

    /// <summary>
    /// The name of slice <paramref name="index"/> of the sheet <paramref name="sheetName"/>: the sheet's name, an
    /// underscore and the index, as the automatic dual-grid tiling rules expect.
    /// </summary>
    public static string SliceName(string sheetName, int index)
    {
        return $"{sheetName}_{index}";
    }

    /// <summary>
    /// Sets the sheet's import settings for pieces of <paramref name="pieceSize"/> px, without reimporting, and returns
    /// whether any changed.
    /// </summary>
    private static bool ApplyImportSettings(TextureImporter importer, int pieceSize)
    {
        bool changed = importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple
            || !Mathf.Approximately(importer.spritePixelsPerUnit, pieceSize) || importer.filterMode != FilterMode.Point
            || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.sRGBTexture;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = pieceSize;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.sRGBTexture = true;
        return changed;
    }

    /// <summary>
    /// Gives the sheet one slice per piece, named <see cref="SliceName"/>, at its rect with a centred pivot, through the
    /// sprite editor's data provider so existing slices keep their IDs, without reimporting. Returns whether any slice
    /// changed or was added.
    /// </summary>
    private static bool Slice(TextureImporter importer, string sheetName, int pieceSize)
    {
        SpriteDataProviderFactories factories = new();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        List<SpriteRect> rects = provider.GetSpriteRects().ToList();
        ISpriteNameFileIdDataProvider fileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> pairs = fileIds.GetNameFileIdPairs().ToList();
        bool changed = false;
        for (int i = 0; i < WallSheetRecipe.PieceCount; i++)
        {
            string sliceName = SliceName(sheetName, i);
            SpriteRect slice = rects.FirstOrDefault(r => r.name == sliceName);
            if (slice == null)
            {
                slice = new SpriteRect { name = sliceName, spriteID = GUID.Generate() };
                rects.Add(slice);
                pairs.Add(new SpriteNameFileIdPair(sliceName, slice.spriteID));
                changed = true;
            }

            RectInt piece = WallSheetRecipe.PieceRect(i, pieceSize);
            Rect wanted = new(piece.x, piece.y, piece.width, piece.height);
            if (slice.rect != wanted || slice.alignment != SpriteAlignment.Center || slice.pivot != new Vector2(0.5f, 0.5f) || slice.border != Vector4.zero)
            {
                slice.rect = wanted;
                slice.alignment = SpriteAlignment.Center;
                slice.pivot = new Vector2(0.5f, 0.5f);
                slice.border = Vector4.zero;
                changed = true;
            }
        }

        if (changed)
        {
            provider.SetSpriteRects(rects.ToArray());
            fileIds.SetNameFileIdPairs(pairs);
            provider.Apply();
        }

        return changed;
    }
}
