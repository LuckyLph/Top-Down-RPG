using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Writes what a <see cref="CharacterRecipe"/> bakes: each clip's sheet as
/// <c>Assets/Sprites/Characters/&lt;sheet name&gt;/&lt;sheet name&gt;_&lt;clip&gt;.png</c>, imported as a
/// point-filtered, uncompressed sprite sheet without mipmaps at 32 pixels per unit (as <see cref="PixelArtTextureDefaults"/>
/// keeps everything under Assets/Sprites), and sliced into one sprite per direction and frame,
/// <c>&lt;sheet name&gt;_&lt;clip&gt;_&lt;direction&gt;_&lt;frame&gt;</c>, at <see cref="CharacterSheet.FrameRect"/> with
/// its pivot on the clip's feet pixel. Slices that already exist keep their sprite IDs; ones no longer baked are
/// removed. Every sheet is baked before any file is written; a sheet whose bytes did not change is not rewritten, and
/// one whose import settings and slices did not change either is not reimported.
/// </summary>
public static class CharacterSheetWriter
{
    public const string Root = "Assets/Sprites/Characters";
    public const int PixelsPerUnit = 32;

    /// <summary>
    /// Bakes and writes every clip of <paramref name="recipe"/>, returning the sheets' paths. Logs an error and writes
    /// nothing when the recipe cannot bake (<see cref="CharacterRecipe.CanGenerate"/>).
    /// </summary>
    public static IReadOnlyList<string> Generate(CharacterRecipe recipe)
    {
        if (!recipe.CanGenerate(out string problem))
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            return null;
        }

        CharacterBaker baker = new(recipe);
        List<CharacterSheet> sheets = recipe.Clips.Select(baker.Bake).ToList();
        TerrainMaterialWriter.EnsureFolder(FolderOf(recipe));
        List<string> paths = new();
        foreach (CharacterSheet sheet in sheets)
        {
            string path = SheetPath(recipe, sheet.Clip);
            byte[] png = TerrainMaterialWriter.EncodePng(sheet.Width, sheet.Height, sheet.Pixels);
            bool written = !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png) || AssetImporter.GetAtPath(path) == null;
            if (written)
            {
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool changed = ApplyImportSettings(importer);
            changed |= Slice(importer, recipe, sheet);
            if (written || changed)
            {
                importer.SaveAndReimport();
            }

            paths.Add(path);
        }

        return paths;
    }

    /// <summary>
    /// The folder of <paramref name="recipe"/>'s sheets.
    /// </summary>
    public static string FolderOf(CharacterRecipe recipe)
    {
        return $"{Root}/{recipe.SheetName}";
    }

    /// <summary>
    /// The path of <paramref name="clip"/>'s sheet.
    /// </summary>
    public static string SheetPath(CharacterRecipe recipe, CharacterClip clip)
    {
        return $"{FolderOf(recipe)}/{recipe.SheetName}_{clip.Name}.png";
    }

    /// <summary>
    /// The name of the slice of frame <paramref name="frame"/> of direction <paramref name="direction"/> of
    /// <paramref name="clip"/>.
    /// </summary>
    public static string SliceName(CharacterRecipe recipe, CharacterClip clip, int direction, int frame)
    {
        return $"{recipe.SheetName}_{clip.Name}_{CharacterDirections.Name(direction)}_{frame}";
    }

    /// <summary>
    /// Sets the sheet's import settings, without reimporting, and returns whether any changed.
    /// </summary>
    private static bool ApplyImportSettings(TextureImporter importer)
    {
        bool changed = importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple
            || !Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit) || importer.filterMode != FilterMode.Point
            || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.sRGBTexture;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.sRGBTexture = true;
        return changed;
    }

    /// <summary>
    /// Gives the sheet one slice per direction and frame, named <see cref="SliceName"/>, at its frame's rect with a
    /// custom pivot on the clip's feet pixel, through the sprite editor's data provider so existing slices keep their
    /// IDs, and removes any other slice, without reimporting. Returns whether any slice changed, was added or removed.
    /// </summary>
    private static bool Slice(TextureImporter importer, CharacterRecipe recipe, CharacterSheet sheet)
    {
        SpriteDataProviderFactories factories = new();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        List<SpriteRect> existing = provider.GetSpriteRects().ToList();
        ISpriteNameFileIdDataProvider fileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> pairs = fileIds.GetNameFileIdPairs().ToList();
        CharacterClip clip = sheet.Clip;
        Vector2 pivot = new(clip.Pivot.x / (float)clip.FrameSize.x, clip.Pivot.y / (float)clip.FrameSize.y);
        List<SpriteRect> rects = new();
        bool changed = false;
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < sheet.FrameCount; frame++)
            {
                string name = SliceName(recipe, clip, direction, frame);
                SpriteRect slice = existing.FirstOrDefault(r => r.name == name);
                if (slice == null)
                {
                    slice = new SpriteRect { name = name, spriteID = GUID.Generate() };
                    pairs.Add(new SpriteNameFileIdPair(name, slice.spriteID));
                    changed = true;
                }

                RectInt frameRect = CharacterSheet.FrameRect(direction, frame, clip.FrameSize);
                Rect wanted = new(frameRect.x, frameRect.y, frameRect.width, frameRect.height);
                if (slice.rect != wanted || slice.alignment != SpriteAlignment.Custom || slice.pivot != pivot || slice.border != Vector4.zero)
                {
                    slice.rect = wanted;
                    slice.alignment = SpriteAlignment.Custom;
                    slice.pivot = pivot;
                    slice.border = Vector4.zero;
                    changed = true;
                }

                rects.Add(slice);
            }
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
}
