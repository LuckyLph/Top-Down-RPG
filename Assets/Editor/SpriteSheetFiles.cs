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
/// changed. A glow written beside a sheet (<see cref="WriteGlow"/>) is a plain texture the sheet takes as a secondary
/// texture (<see cref="SetSecondaryTextures"/>), the props' and the characters' lanterns' alike.
/// </summary>
public static class SpriteSheetFiles
{
    public const string GlowTextureName = "_GlowTex";

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
    /// Writes <paramref name="pixels"/> as a PNG at <paramref name="path"/> when its bytes changed and imports it as a
    /// glow: a plain texture with no sprites, point filtered and clamped, without mipmaps or compression, its colour in
    /// sRGB as the sheet's. A new glow is imported first as one clear pixel with those settings, so the project's 2D
    /// import, which cuts a new file into sprites on its first import, finds nothing to cut. Returns whether the file
    /// or its import settings changed.
    /// </summary>
    public static bool WriteGlow(string path, int width, int height, Color32[] pixels)
    {
        int side = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
        bool created = AssetImporter.GetAtPath(path) == null;
        if (created)
        {
            TerrainMaterialWriter.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            File.WriteAllBytes(path, TerrainMaterialWriter.EncodePng(1, 1, new Color32[1]));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplyGlowSettings((TextureImporter)AssetImporter.GetAtPath(path), side);
        }

        byte[] png = TerrainMaterialWriter.EncodePng(width, height, pixels);
        bool written = !File.ReadAllBytes(path).SequenceEqual(png);
        if (written)
        {
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        bool changed = ApplyGlowSettings((TextureImporter)AssetImporter.GetAtPath(path), side);
        return created || written || changed;
    }

    /// <summary>
    /// Gives a glow's importer its settings (see <see cref="WriteGlow"/>), a maximum size of at least
    /// <paramref name="side"/>, reimporting it when any changed. Returns whether any did.
    /// </summary>
    private static bool ApplyGlowSettings(TextureImporter importer, int side)
    {
        bool changed = importer.textureType != TextureImporterType.Default || importer.spriteImportMode != SpriteImportMode.None || !importer.sRGBTexture
            || importer.filterMode != FilterMode.Point || importer.wrapMode != TextureWrapMode.Clamp || importer.mipmapEnabled
            || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.npotScale != TextureImporterNPOTScale.None
            || importer.isReadable || importer.alphaIsTransparency || importer.maxTextureSize < side;
        if (!changed)
        {
            return false;
        }

        importer.textureType = TextureImporterType.Default;
        importer.spriteImportMode = SpriteImportMode.None;
        importer.sRGBTexture = true;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        importer.alphaIsTransparency = false;
        importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, side);
        importer.SaveAndReimport();
        return true;
    }

    /// <summary>
    /// Gives the sheet at <paramref name="sheet"/> exactly the secondary textures <paramref name="wanted"/>, reimporting
    /// it when they differ. Returns whether they changed.
    /// </summary>
    public static bool SetSecondaryTextures(string sheet, SecondarySpriteTexture[] wanted)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(sheet);
        if (SameSecondaryTextures(importer.secondarySpriteTextures, wanted))
        {
            return false;
        }

        importer.secondarySpriteTextures = wanted;
        importer.SaveAndReimport();
        return true;
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
    /// Whether <paramref name="a"/> and <paramref name="b"/> name the same textures in the same order.
    /// </summary>
    private static bool SameSecondaryTextures(SecondarySpriteTexture[] a, SecondarySpriteTexture[] b)
    {
        a ??= new SecondarySpriteTexture[0];
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].name != b[i].name || a[i].texture != b[i].texture)
            {
                return false;
            }
        }

        return true;
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
