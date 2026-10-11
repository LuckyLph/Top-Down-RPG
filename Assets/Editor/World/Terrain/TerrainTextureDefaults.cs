using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import postprocessor for the terrain material textures under Assets/Textures/Terrain (written by
/// tools/terrain-art/materials.py): plain textures with sRGB off (the terrain shader treats albedo bytes as sRGB
/// itself and reads the cell fields as raw offsets), point filtering, repeat wrapping, no mipmaps, no NPOT scaling and
/// uncompressed RGBA32, so <see cref="TerrainBlendRenderer"/> can copy them into its texture arrays as they are.
/// </summary>
public sealed class TerrainTextureDefaults : AssetPostprocessor
{
    private const string TerrainTexturesRoot = "Assets/Textures/Terrain/";

    /// <summary>
    /// Applies the terrain texture settings to a texture under Assets/Textures/Terrain before it imports; other
    /// textures are left alone.
    /// </summary>
    private void OnPreprocessTexture()
    {
        if (assetImporter is not TextureImporter importer
            || !importer.assetPath.Replace('\\', '/').StartsWith(TerrainTexturesRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();
        settings.textureCompression = TextureImporterCompression.Uncompressed;
        settings.format = TextureImporterFormat.RGBA32;
        importer.SetPlatformTextureSettings(settings);
    }
}
