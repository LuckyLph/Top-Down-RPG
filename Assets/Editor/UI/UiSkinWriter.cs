using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes what a <see cref="UiSkinRecipe"/> draws as the skin's sprites in <see cref="UiSkinRecipe.Folder"/>: each
/// piece's PNG, then its import settings, a single sprite with a centred pivot and the piece's 9-slice border.
/// <see cref="PixelArtTextureDefaults"/> imports it at 32 pixels per unit with point filtering, so one sprite pixel is one
/// UI px, or one art px in the world. The pixels depend only on the recipe and the encoding only on the pixels, so a
/// rerun writes the same bytes, and an unchanged sprite is not reimported.
/// </summary>
public static class UiSkinWriter
{
    /// <summary>
    /// Writes every piece of <paramref name="recipe"/>. Logs an error and writes nothing when its settings cannot draw
    /// them.
    /// </summary>
    public static bool Generate(UiSkinRecipe recipe)
    {
        if (!recipe.CanGenerate(out string problem))
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            return false;
        }

        Directory.CreateDirectory(UiSkinRecipe.Folder);
        foreach (UiSkinPiece piece in UiSkinRecipe.Pieces)
        {
            Write(recipe.Generate(piece));
        }

        return true;
    }

    /// <summary>
    /// Writes one piece's PNG when its bytes changed, imports it when new, and sets its sprite settings, reimporting
    /// only when the file or a setting changed.
    /// </summary>
    private static void Write(SkinSprite sprite)
    {
        string path = UiSkinRecipe.PathOf(sprite.Piece);
        byte[] png = TerrainMaterialWriter.EncodePng(sprite.Width, sprite.Height, sprite.Pixels);
        bool exists = File.Exists(path);
        bool written = !exists || !File.ReadAllBytes(path).SequenceEqual(png);
        if (written)
        {
            File.WriteAllBytes(path, png);
        }

        if (!exists)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = importer.spriteImportMode != SpriteImportMode.Single || importer.spriteBorder != sprite.Border
            || importer.spritePivot != new Vector2(0.5f, 0.5f);
        TextureImporterSettings settings = new();
        importer.ReadTextureSettings(settings);
        changed |= settings.spriteAlignment != (int)SpriteAlignment.Center || settings.spriteMeshType != SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = sprite.Border;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        if (written || changed)
        {
            importer.SaveAndReimport();
        }
    }
}
