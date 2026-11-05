using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import postprocessor for the terrain material textures under Assets/Textures/Terrain, a folder per material (written
/// by <see cref="TerrainMaterialWriter"/>): plain textures with sRGB off (the terrain shader treats albedo bytes as sRGB
/// itself and reads the cell fields as raw offsets), point filtering, repeat wrapping, no mipmaps, no NPOT scaling and
/// uncompressed RGBA32, so <see cref="TerrainBlendRenderer"/> can copy them into its texture arrays as they are. It also
/// gives a chunk's baked coast and glow maps their import settings (<see cref="WorldAuthoring.ApplyCoastImport"/>,
/// <see cref="WorldAuthoring.ApplyGlowImport"/>) as they first import, so a new map imports once (Docs/WorldGeneration.md,
/// Phase 6).
/// </summary>
public sealed class TerrainTextureDefaults : AssetPostprocessor
{
    private const string TerrainTexturesRoot = "Assets/Textures/Terrain/";

    /// <summary>
    /// Applies the terrain texture settings to a texture under Assets/Textures/Terrain (in any of its folders), and a
    /// baked map's to a chunk's coast or glow map, before it imports; other textures are left alone.
    /// </summary>
    private void OnPreprocessTexture()
    {
        if (assetImporter is not TextureImporter importer)
        {
            return;
        }

        string path = importer.assetPath.Replace('\\', '/');
        if (WorldAuthoring.IsCoastMapPath(path))
        {
            WorldAuthoring.ApplyCoastImport(importer);
            return;
        }

        if (WorldAuthoring.IsGlowMapPath(path))
        {
            WorldAuthoring.ApplyGlowImport(importer);
            return;
        }

        if (!path.StartsWith(TerrainTexturesRoot, StringComparison.OrdinalIgnoreCase))
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
