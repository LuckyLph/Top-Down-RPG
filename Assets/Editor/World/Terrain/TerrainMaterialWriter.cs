using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Writes what a <see cref="TerrainMaterialRecipe"/> generates as its <see cref="TerrainMaterial"/>'s textures: PNGs in
/// the material's own folder under Assets/Textures/Terrain, at the paths the material's textures already have there (so
/// their GUIDs stay, even after the material is renamed) or else named after the material, reimported so
/// <see cref="TerrainTextureDefaults"/>
/// applies, and the material pointed at them with the base colour, repeat and cell mode they were made for, after which
/// the renderers drawing the material rebuild, and the swatch of its ground tile (<see cref="TerrainTileWriter"/>)
/// follows. It refuses to overwrite another material's texture, and writes all of a material's files or none. The
/// pixels depend only on the recipe and its seed, and the PNG encoding only on the pixels, so a rerun writes
/// byte-identical files.
/// </summary>
public static class TerrainMaterialWriter
{
    public const string TextureFolder = "Assets/Textures/Terrain";

    /// <summary>
    /// Generates <paramref name="recipe"/>'s textures for its seed and writes them as its material's, saving the
    /// material, and rewrites the swatch of the material's ground tile when it has one
    /// (<see cref="TerrainTileWriter.RefreshSwatch"/>). Logs an error and writes nothing when the recipe has no
    /// material, or when a file it would write is another material's texture or would take two of the textures.
    /// </summary>
    public static bool Generate(TerrainMaterialRecipe recipe)
    {
        TerrainMaterial material = recipe.Material;
        if (material == null)
        {
            Debug.LogError($"{recipe.name} has no terrain material to fill.", recipe);
            return false;
        }

        TerrainTextures textures = recipe.Generate(recipe.Seed);
        (string Albedo, string CellField, string Height) paths = TargetPaths(material, textures.CellField != null);
        if (!CanWrite(recipe, material, paths))
        {
            return false;
        }

        foreach (string path in new[] { paths.Albedo, paths.CellField, paths.Height })
        {
            if (path != null)
            {
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            }
        }

        WriteFiles(textures, paths);
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in new[] { paths.Albedo, paths.CellField, paths.Height })
            {
                if (path != null)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        Apply(material, textures, Load(paths.Albedo), Load(paths.CellField), Load(paths.Height));
        AssetDatabase.SaveAssetIfDirty(material);
        TerrainTileWriter.RefreshSwatch(material, textures);
        return true;
    }

    /// <summary>
    /// The start of the texture file names of <paramref name="material"/>: its name in lower case.
    /// </summary>
    public static string FileBaseName(TerrainMaterial material)
    {
        return material.name.ToLowerInvariant();
    }

    /// <summary>
    /// The folder <paramref name="material"/>'s textures go in: the folder of the first of its textures (albedo, cell
    /// field, height) that is a PNG under <see cref="TextureFolder"/>, so a material's textures stay together and a
    /// renamed material keeps its folder, else a folder named after the material in <see cref="TextureFolder"/>.
    /// </summary>
    public static string FolderOf(TerrainMaterial material)
    {
        string existing = PathInTextureFolder(material.Albedo) ?? PathInTextureFolder(material.CellField) ?? PathInTextureFolder(material.Height);
        return existing != null ? Path.GetDirectoryName(existing).Replace('\\', '/') : $"{TextureFolder}/{material.name}";
    }

    /// <summary>
    /// Where Generate writes <paramref name="material"/>'s textures: for each, the path of the PNG the material already
    /// uses under <see cref="TextureFolder"/>, else the file named after the material in its folder
    /// (<see cref="FolderOf"/>). The cell field's path is null when <paramref name="withCellField"/> is false.
    /// </summary>
    public static (string Albedo, string CellField, string Height) TargetPaths(TerrainMaterial material, bool withCellField)
    {
        (string Albedo, string CellField, string Height) named = PathsIn(FolderOf(material), FileBaseName(material), withCellField);
        return (PathInTextureFolder(material.Albedo) ?? named.Albedo,
            withCellField ? PathInTextureFolder(material.CellField) ?? named.CellField : null,
            PathInTextureFolder(material.Height) ?? named.Height);
    }

    /// <summary>
    /// The paths <paramref name="baseName"/>_albedo.png, _field.png (null when <paramref name="withCellField"/> is
    /// false) and _height.png in <paramref name="folder"/>.
    /// </summary>
    public static (string Albedo, string CellField, string Height) PathsIn(string folder, string baseName, bool withCellField)
    {
        return ($"{folder}/{baseName}_albedo.png", withCellField ? $"{folder}/{baseName}_field.png" : null, $"{folder}/{baseName}_height.png");
    }

    /// <summary>
    /// The first terrain material asset other than <paramref name="except"/> with a texture at
    /// <paramref name="path"/>, or null when there is none.
    /// </summary>
    public static TerrainMaterial MaterialUsingTexture(string path, TerrainMaterial except)
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(TerrainMaterial)}"))
        {
            TerrainMaterial other = AssetDatabase.LoadAssetAtPath<TerrainMaterial>(AssetDatabase.GUIDToAssetPath(guid));
            if (other != null && other != except
                && (AssetPath(other.Albedo) == path || AssetPath(other.CellField) == path || AssetPath(other.Height) == path))
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes <paramref name="textures"/> to <paramref name="paths"/>, replacing the files there: all of them, or none
    /// if one cannot be written. Every PNG is encoded before any file is touched, and when a write fails the files
    /// already written get their previous bytes back (or are deleted if they are new) before the error is rethrown.
    /// Imports nothing.
    /// </summary>
    public static void WriteFiles(TerrainTextures textures, (string Albedo, string CellField, string Height) paths)
    {
        if (textures.CellField != null && paths.CellField == null)
        {
            throw new ArgumentException("The textures have a cell field but no path was given for it.", nameof(paths));
        }

        List<string> targets = new() { paths.Albedo };
        List<byte[]> contents = new() { EncodePng(textures.Size, textures.Albedo) };
        if (textures.CellField != null)
        {
            targets.Add(paths.CellField);
            contents.Add(EncodePng(textures.Size, textures.CellField));
        }

        targets.Add(paths.Height);
        contents.Add(EncodePng(textures.Size, textures.Height));
        byte[][] previous = new byte[targets.Count][];
        for (int i = 0; i < targets.Count; i++)
        {
            previous[i] = File.Exists(targets[i]) ? File.ReadAllBytes(targets[i]) : null;
        }

        int written = 0;
        try
        {
            for (; written < targets.Count; written++)
            {
                File.WriteAllBytes(targets[written], contents[written]);
            }
        }
        catch
        {
            for (int i = 0; i < written; i++)
            {
                if (previous[i] != null)
                {
                    File.WriteAllBytes(targets[i], previous[i]);
                }
                else
                {
                    File.Delete(targets[i]);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// The PNG bytes of a <paramref name="size"/> px square RGBA texture of <paramref name="pixels"/>, rows from the
    /// bottom as <see cref="Texture2D.SetPixels32"/> takes them; colour is kept where alpha is 0.
    /// </summary>
    public static byte[] EncodePng(int size, Color32[] pixels)
    {
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false, true);
        try
        {
            texture.SetPixels32(pixels);
            return texture.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// Points <paramref name="material"/> at the given textures and writes the base colour, repeat and cell mode
    /// <paramref name="textures"/> were made for, through its serialized data and without undo (the files they came
    /// from cannot be undone either). Then has the renderers drawing it rebuild, after the editor's current update as
    /// for an inspector change, even when nothing on the material changed, since the textures' pixels may have.
    /// </summary>
    public static void Apply(TerrainMaterial material, TerrainTextures textures, Texture2D albedo, Texture2D cellField, Texture2D height)
    {
        SerializedObject serialized = new(material);
        serialized.FindProperty("albedo").objectReferenceValue = albedo;
        serialized.FindProperty("cellField").objectReferenceValue = cellField;
        serialized.FindProperty("height").objectReferenceValue = height;
        serialized.FindProperty("repeatCells").intValue = textures.Size / TerrainStyle.PixelsPerCell;
        serialized.FindProperty("cellMode").enumValueIndex = (int)textures.CellMode;
        serialized.FindProperty("baseColour").colorValue = textures.BaseColour;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        material.NotifyChanged();
    }

    /// <summary>
    /// Logs an error and returns false when two of <paramref name="paths"/> are the same file or one of them is another
    /// material's texture.
    /// </summary>
    private static bool CanWrite(TerrainMaterialRecipe recipe, TerrainMaterial material, (string Albedo, string CellField, string Height) paths)
    {
        if (paths.Albedo == paths.Height || paths.Albedo == paths.CellField || paths.Height == paths.CellField)
        {
            Debug.LogError($"{recipe.name}: {material.name} uses one file for two of its textures; give each its own before generating.", material);
            return false;
        }

        foreach (string path in new[] { paths.Albedo, paths.CellField, paths.Height })
        {
            TerrainMaterial other = path != null ? MaterialUsingTexture(path, material) : null;
            if (other != null)
            {
                Debug.LogError($"{recipe.name}: {path} is a texture of {other.name} ({AssetDatabase.GetAssetPath(other)}), so generating {material.name} would overwrite it. Rename {material.name} or point it at its own textures.", recipe);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Creates <paramref name="folder"/> and any missing parent folder as asset folders.
    /// </summary>
    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    /// <summary>
    /// The asset path of <paramref name="texture"/> when it is a PNG under <see cref="TextureFolder"/> (in any of its
    /// folders), else null.
    /// </summary>
    private static string PathInTextureFolder(Texture2D texture)
    {
        string path = AssetPath(texture);
        return path != null && path.StartsWith(TextureFolder + "/", StringComparison.Ordinal) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>
    /// The asset path of <paramref name="asset"/>, or null when it is unset or not an asset.
    /// </summary>
    private static string AssetPath(Object asset)
    {
        string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// The texture at <paramref name="path"/>, or null for no path.
    /// </summary>
    private static Texture2D Load(string path)
    {
        return path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
    }
}
