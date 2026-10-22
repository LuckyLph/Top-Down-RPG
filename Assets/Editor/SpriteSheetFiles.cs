using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Writes generated sprite sheets (the weapon sprites and the effects): a PNG imported as a pixel art sprite sheet at
/// 32 pixels per unit (<see cref="CharacterSheetWriter.ApplyImportSettings"/>), sliced as asked. A sheet whose bytes did
/// not change is not rewritten, existing slices keep their sprite IDs, and the sheet is reimported only when anything
/// changed.
/// </summary>
public static class SpriteSheetFiles
{
    /// <summary>
    /// Writes <paramref name="pixels"/> (rows from the bottom) as a PNG at <paramref name="path"/> when its bytes
    /// changed, imports it as a pixel art sprite sheet and gives it exactly <paramref name="slices"/>.
    /// </summary>
    public static void WriteSheet(string path, int width, int height, Color32[] pixels, IReadOnlyList<Slice> slices)
    {
        byte[] png = TerrainMaterialWriter.EncodePng(width, height, pixels);
        bool written = !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png) || AssetImporter.GetAtPath(path) == null;
        if (written)
        {
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = CharacterSheetWriter.ApplyImportSettings(importer);
        changed |= ApplySlices(importer, slices);
        if (written || changed)
        {
            importer.SaveAndReimport();
        }
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
    }
}
