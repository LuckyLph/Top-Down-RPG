using System;
using UnityEditor;
using UnityEditor.U2D.Aseprite;
using UnityEngine;

/// <summary>
/// Import postprocessor that keeps the Aseprite importer's own output light for every <c>.aseprite</c> file under
/// <see cref="AsepriteCharacterRecipe.ArtRoot"/> (Docs/AsepriteCharacters.md, Import settings): Animated Sprite mode,
/// Merge Frame layer import, hidden layers left out, 32 pixels per unit, point filtering, no mipmaps, no compression,
/// no model prefab and no animation clips (Mecanim is not used). Its texture and sprites only preview the file in the
/// Project window; <see cref="AsepriteCharacterWriter"/> reads the file itself, so no pivot is set. Other Aseprite
/// files are left alone.
/// </summary>
public sealed class AsepriteImportSettings : AssetPostprocessor
{
    /// <summary>
    /// Applies the settings to a character's file before it imports.
    /// </summary>
    void OnPreprocessAsset()
    {
        if (assetImporter is AsepriteImporter importer && IsCharacterArt(assetPath))
        {
            Apply(importer);
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> lies under <see cref="AsepriteCharacterRecipe.ArtRoot"/>, ignoring case and
    /// slash direction.
    /// </summary>
    public static bool IsCharacterArt(string path)
    {
        return path.Replace('\\', '/').StartsWith(AsepriteCharacterRecipe.ArtRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the settings into <paramref name="importer"/> without reimporting, and returns whether any changed.
    /// </summary>
    public static bool Apply(AsepriteImporter importer)
    {
        bool changed = false;
        if (importer.importMode != FileImportModes.AnimatedSprite)
        {
            importer.importMode = FileImportModes.AnimatedSprite;
            changed = true;
        }

        if (importer.layerImportMode != LayerImportModes.MergeFrame)
        {
            importer.layerImportMode = LayerImportModes.MergeFrame;
            changed = true;
        }

        if (importer.includeHiddenLayers)
        {
            importer.includeHiddenLayers = false;
            changed = true;
        }

        if (!Mathf.Approximately(importer.spritePixelsPerUnit, CharacterSheetWriter.PixelsPerUnit))
        {
            importer.spritePixelsPerUnit = CharacterSheetWriter.PixelsPerUnit;
            changed = true;
        }

        if (importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (importer.generateModelPrefab)
        {
            importer.generateModelPrefab = false;
            changed = true;
        }

        if (importer.generateAnimationClips)
        {
            importer.generateAnimationClips = false;
            changed = true;
        }

        TextureImporterPlatformSettings platform = importer.GetImporterPlatformSettings(EditorUserBuildSettings.activeBuildTarget);
        if (platform.textureCompression != TextureImporterCompression.Uncompressed)
        {
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetImporterPlatformSettings(platform);
            changed = true;
        }

        return changed;
    }
}
