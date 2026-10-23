using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import postprocessor that enforces pixel art settings on every texture under Assets/Sprites: sprite type, 32 pixels
/// per unit, point filtering, no mipmaps or NPOT scaling, and uncompressed RGBA32 on each platform, each platform's
/// maximum size at least the default one's (so a sheet raised past 2048 pixels is not scaled down).
/// </summary>
public sealed class PixelArtTextureDefaults : AssetPostprocessor
{
    private const string SpritesRoot = "Assets/Sprites/";
    private const int PixelsPerUnit = 32;

    /// <summary>
    /// Applies the pixel art defaults to a texture under Assets/Sprites before it imports; other textures are left
    /// alone.
    /// </summary>
    void OnPreprocessTexture()
    {
        if (assetImporter is not TextureImporter importer)
        {
            return;
        }

        if (!IsManagedSpritePath(importer.assetPath))
        {
            return;
        }

        ApplyPixelArtDefaults(importer);
    }

    /// <summary>
    /// Applies the pixel art defaults to every texture already under Assets/Sprites, reimports only the ones that
    /// changed and logs how many were updated.
    /// </summary>
    [MenuItem("Tools/Textures/Apply Pixel Art Defaults To Sprites")]
    private static void ApplyDefaultsToExistingSprites()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Sprites" });
        int scannedCount = 0;
        int updatedCount = 0;

        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !IsManagedSpritePath(path))
            {
                continue;
            }

            scannedCount++;
            if (!ApplyPixelArtDefaults(importer))
            {
                continue;
            }

            importer.SaveAndReimport();
            updatedCount++;
        }

        if (updatedCount == 0)
        {
            Debug.Log($"Scanned {scannedCount} texture(s) in Assets/Sprites. All matching textures already had the pixel art defaults.");
            return;
        }

        Debug.Log($"Applied pixel art defaults to {updatedCount} of {scannedCount} texture(s) in Assets/Sprites.");
    }

    /// <summary>
    /// Whether <paramref name="path"/> lies under Assets/Sprites, ignoring case and slash direction.
    /// </summary>
    private static bool IsManagedSpritePath(string path)
    {
        return path.Replace('\\', '/').StartsWith(SpritesRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the pixel art settings into <paramref name="importer"/> without reimporting, and returns whether any
    /// setting changed.
    /// </summary>
    private static bool ApplyPixelArtDefaults(TextureImporter importer)
    {
        bool changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (Math.Abs(importer.spritePixelsPerUnit - PixelsPerUnit) > Mathf.Epsilon)
        {
            importer.spritePixelsPerUnit = PixelsPerUnit;
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

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (importer.npotScale != TextureImporterNPOTScale.None)
        {
            importer.npotScale = TextureImporterNPOTScale.None;
            changed = true;
        }

        changed |= SetPlatformCompression(importer, "DefaultTexturePlatform");
        changed |= SetPlatformCompression(importer, "Standalone");
        changed |= SetPlatformCompression(importer, "WebGL");
        changed |= SetPlatformCompression(importer, "Android");
        changed |= SetPlatformCompression(importer, "iPhone");

        return changed;
    }

    /// <summary>
    /// Makes <paramref name="platformName"/> uncompressed, overriding it to RGBA32 on every platform except the
    /// default one with a maximum size no smaller than the default one's, and returns whether anything changed.
    /// </summary>
    private static bool SetPlatformCompression(TextureImporter importer, string platformName)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platformName);
        bool changed = false;

        bool shouldOverride = platformName != "DefaultTexturePlatform";
        if (settings.overridden != shouldOverride)
        {
            settings.overridden = shouldOverride;
            changed = true;
        }

        if (settings.textureCompression != TextureImporterCompression.Uncompressed)
        {
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (settings.overridden && settings.format != TextureImporterFormat.RGBA32)
        {
            settings.format = TextureImporterFormat.RGBA32;
            changed = true;
        }

        if (settings.overridden && settings.maxTextureSize < importer.maxTextureSize)
        {
            settings.maxTextureSize = importer.maxTextureSize;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        importer.SetPlatformTextureSettings(settings);
        return true;
    }
}
