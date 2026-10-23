using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Writes generated sprite sheets and the assets beside them (the characters' sheets and animation sets, the weapon
/// sprites, the effects, and the same drawn by hand in Aseprite): a PNG imported as a pixel art sprite sheet at 32
/// pixels per unit (<see cref="CharacterSheetWriter.ApplyImportSettings"/>), sliced as asked. A sheet whose bytes did not
/// change is not rewritten, existing slices keep their sprite IDs by name, slices no longer asked for are removed, and
/// the sheet is reimported only when anything changed. A sheet already imported gets its new slices before its new
/// pixels import, so one that lost frames never imports with slices past its edge, and a sheet larger than its import's
/// maximum size gets the size it needs. An asset written beside a sheet keeps its GUID and is saved only when it
/// changed.
/// </summary>
public static class SpriteSheetFiles
{
    /// <summary>
    /// Writes <paramref name="pixels"/> (rows from the bottom) as a PNG at <paramref name="path"/> when its bytes
    /// changed, imports it as a pixel art sprite sheet and gives it exactly <paramref name="slices"/>. Returns whether
    /// the file was written or its import settings or slices changed.
    /// </summary>
    public static bool WriteSheet(string path, int width, int height, Color32[] pixels, IReadOnlyList<Slice> slices)
    {
        byte[] png = TerrainMaterialWriter.EncodePng(width, height, pixels);
        bool imported = AssetImporter.GetAtPath(path) != null;
        bool written = !imported || !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png);
        if (written)
        {
            File.WriteAllBytes(path, png);
        }

        if (!imported)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = CharacterSheetWriter.ApplyImportSettings(importer);
        changed |= FitMaxSize(importer, Mathf.Max(width, height));
        changed |= ApplySlices(importer, slices);
        if (written || changed)
        {
            importer.SaveAndReimport();
        }

        return written || changed;
    }

    /// <summary>
    /// The sprites of the sheet at <paramref name="path"/> by slice name.
    /// </summary>
    public static Dictionary<string, Sprite> SpritesByName(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
    }

    /// <summary>
    /// Writes the unsaved asset <paramref name="fresh"/> at <paramref name="path"/>, named after the file: creates it,
    /// or copies it over the existing asset when they differ, keeping its GUID, and destroys it. Returns whether the
    /// asset was created or changed.
    /// </summary>
    public static bool WriteAsset<T>(string path, T fresh) where T : ScriptableObject
    {
        string assetName = Path.GetFileNameWithoutExtension(path);
        fresh.name = assetName;
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return true;
        }

        bool changed = EditorJsonUtility.ToJson(existing) != EditorJsonUtility.ToJson(fresh);
        if (changed)
        {
            EditorUtility.CopySerialized(fresh, existing);
            existing.name = assetName;
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
        }

        Object.DestroyImmediate(fresh);
        return changed;
    }

    /// <summary>
    /// Raises the import's maximum size to the smallest one that holds a sheet of <paramref name="side"/> pixels, its
    /// longer side, when it is smaller, without reimporting; returns whether it changed. Smaller sheets keep theirs.
    /// </summary>
    private static bool FitMaxSize(TextureImporter importer, int side)
    {
        if (importer.maxTextureSize >= side)
        {
            return false;
        }

        int size = importer.maxTextureSize;
        while (size < side)
        {
            size *= 2;
        }

        importer.maxTextureSize = size;
        return true;
    }

    /// <summary>
    /// Gives the sheet exactly <paramref name="slices"/> through the sprite editor's data provider, so existing slices
    /// keep their IDs, without reimporting. Returns whether anything changed.
    /// </summary>
    private static bool ApplySlices(TextureImporter importer, IReadOnlyList<Slice> slices)
    {
        SpriteDataProviderFactories factories = new();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        List<SpriteRect> existing = provider.GetSpriteRects().ToList();
        ISpriteNameFileIdDataProvider fileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> pairs = fileIds.GetNameFileIdPairs().ToList();
        List<SpriteRect> rects = new();
        bool changed = false;
        foreach (Slice wanted in slices)
        {
            SpriteRect slice = existing.FirstOrDefault(r => r.name == wanted.Name);
            if (slice == null)
            {
                slice = new SpriteRect { name = wanted.Name, spriteID = GUID.Generate() };
                pairs.Add(new SpriteNameFileIdPair(wanted.Name, slice.spriteID));
                changed = true;
            }

            if (slice.rect != wanted.Rect || slice.alignment != SpriteAlignment.Custom || slice.pivot != wanted.Pivot || slice.border != Vector4.zero)
            {
                slice.rect = wanted.Rect;
                slice.alignment = SpriteAlignment.Custom;
                slice.pivot = wanted.Pivot;
                slice.border = Vector4.zero;
                changed = true;
            }

            rects.Add(slice);
        }

        changed |= rects.Count != existing.Count;
        if (!changed)
        {
            return false;
        }

        HashSet<string> kept = new(rects.Select(r => r.name));
        provider.SetSpriteRects(rects.ToArray());
        fileIds.SetNameFileIdPairs(pairs.Where(p => kept.Contains(p.name)).ToList());
        provider.Apply();
        return true;
    }

    /// <summary>
    /// One sprite of a sheet: its name, its rectangle in pixels from the bottom left and its pivot as a share of that
    /// rectangle.
    /// </summary>
    public readonly struct Slice
    {
        /// <summary>
        /// Creates a slice named <paramref name="name"/> over <paramref name="rect"/>, pivoted on its middle.
        /// </summary>
        public Slice(string name, Rect rect)
            : this(name, rect, new Vector2(0.5f, 0.5f))
        {
        }

        /// <summary>
        /// Creates a slice named <paramref name="name"/> over <paramref name="rect"/>, pivoted at
        /// <paramref name="pivot"/> (a share of its size from its bottom left).
        /// </summary>
        public Slice(string name, Rect rect, Vector2 pivot)
        {
            Name = name;
            Rect = rect;
            Pivot = pivot;
        }

        public string Name { get; }
        public Rect Rect { get; }
        public Vector2 Pivot { get; }

        /// <summary>
        /// The pivot of a frame of <paramref name="frameSize"/> on the <paramref name="pixel"/> from its bottom left, as
        /// a share of the frame: the pixel's bottom left corner, as a character's feet and an effect's pivot are named.
        /// </summary>
        public static Vector2 PivotOn(Vector2Int pixel, Vector2Int frameSize)
        {
            return new Vector2(pixel.x / (float)frameSize.x, pixel.y / (float)frameSize.y);
        }
    }
}
