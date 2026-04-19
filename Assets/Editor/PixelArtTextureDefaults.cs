using System;
using UnityEditor;
using UnityEngine;

public sealed class PixelArtTextureDefaults : AssetPostprocessor
{
    private const string SpritesRoot = "Assets/Sprites/";
    private const int PixelsPerUnit = 32;

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

    private static bool IsManagedSpritePath(string path)
    {
        return path.Replace('\\', '/').StartsWith(SpritesRoot, StringComparison.OrdinalIgnoreCase);
    }

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

        if (!changed)
        {
            return false;
        }

        importer.SetPlatformTextureSettings(settings);
        return true;
    }
}
