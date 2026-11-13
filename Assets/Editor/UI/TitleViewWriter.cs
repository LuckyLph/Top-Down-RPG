using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes what a <see cref="TitleViewRecipe"/> draws into <see cref="TitleViewRecipe.Folder"/> (Docs/UiRevamp.md, Phase
/// 11): the logo at once, and the title view a few editor updates after its world's scenes open
/// (<see cref="TitleViewRender"/>), each as a single sprite with a centred pivot, which
/// <see cref="PixelArtTextureDefaults"/> imports at 32 pixels per unit with point filtering, one sprite pixel a UI px.
/// The encoding depends only on the pixels, so a rerun over an unchanged world writes the same bytes and reimports
/// nothing.
/// </summary>
public static class TitleViewWriter
{
    public const int SettleUpdates = 3;

    /// <summary>
    /// Writes the logo of <paramref name="recipe"/>; logs an error and writes nothing when its settings cannot draw it.
    /// </summary>
    public static bool WriteLogo(TitleViewRecipe recipe)
    {
        if (!recipe.CanGenerate(out string problem))
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            return false;
        }

        LogoPicture logo = recipe.GenerateLogo();
        Write(TitleViewRecipe.LogoPath, logo.Width, logo.Height, logo.Pixels);
        return true;
    }

    /// <summary>
    /// Renders the title view of <paramref name="recipe"/> once its world's scenes have drawn for
    /// <see cref="SettleUpdates"/> editor updates, writes it, puts the open scenes back, and tells
    /// <paramref name="done"/> whether it was written. Refuses, with an error, while an open scene has unsaved changes.
    /// </summary>
    public static void WriteView(TitleViewRecipe recipe, Action<bool> done)
    {
        if (!recipe.CanGenerate(out string problem))
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            done?.Invoke(false);
            return;
        }

        TitleViewRender render;
        try
        {
            render = new TitleViewRender(recipe);
        }
        catch (InvalidOperationException exception)
        {
            Debug.LogError(exception.Message, recipe);
            done?.Invoke(false);
            return;
        }

        int updates = 0;
        void Step()
        {
            if (++updates < SettleUpdates)
            {
                return;
            }

            EditorApplication.update -= Step;
            bool written = false;
            try
            {
                Texture2D pixels = render.Render();
                Write(TitleViewRecipe.ViewPath, pixels.width, pixels.height, pixels.GetPixels32());
                UnityEngine.Object.DestroyImmediate(pixels);
                written = true;
            }
            finally
            {
                render.Dispose();
                done?.Invoke(written);
            }
        }

        EditorApplication.update += Step;
    }

    /// <summary>
    /// Writes <paramref name="pixels"/> as the PNG at <paramref name="path"/> when its bytes changed, imports it when
    /// new, and makes it a single sprite with a centred pivot, reimporting only when the file or a setting changed.
    /// </summary>
    internal static void Write(string path, int width, int height, Color32[] pixels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        byte[] png = TerrainMaterialWriter.EncodePng(width, height, pixels);
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
        TextureImporterSettings settings = new();
        importer.ReadTextureSettings(settings);
        bool changed = importer.spriteImportMode != SpriteImportMode.Single || importer.spritePivot != new Vector2(0.5f, 0.5f)
            || settings.spriteAlignment != (int)SpriteAlignment.Center || settings.spriteMeshType != SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        if (written || changed)
        {
            importer.SaveAndReimport();
        }
    }
}
