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
/// one whose import settings and slices did not change either is not reimported. Then it writes the runtime
/// <see cref="CharacterAnimationSet"/>, <c>&lt;sheet name&gt;_AnimationSet.asset</c> beside the sheets: per clip its
/// sprites by direction and frame (and gait frame, for a motion drawn over a gait, whose sheet columns run gait frame
/// by gait frame), each frame's duration at natural speed, its gait, whether it is armed, its contact (release) frame
/// and a gait's natural speed (<see cref="NaturalSpeed"/>); the asset keeps its GUID and is saved only when it changed.
/// A sheet larger than its import's maximum size (a cast drawn over the run's eight frames) gets the size it needs.
/// Writing a sheet (<see cref="WriteSheet"/>) and a set (<see cref="WriteSet"/>) is shared with
/// <see cref="AsepriteCharacterWriter"/>, which writes hand-drawn clips the same way.
/// </summary>
public static class CharacterSheetWriter
{
    public const string Root = "Assets/Sprites/Characters";
    public const int PixelsPerUnit = 32;

    /// <summary>
    /// Bakes and writes every clip of <paramref name="recipe"/>, then its animation set, returning the sheets' paths
    /// followed by the set's. Logs an error and writes nothing when the recipe cannot bake
    /// (<see cref="CharacterRecipe.CanGenerate"/>).
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
        List<string> paths = new();
        foreach (CharacterSheet sheet in sheets)
        {
            CharacterClip clip = sheet.Clip;
            WriteSheet(recipe.SheetName, clip.Name, clip.FrameSize, clip.Pivot, sheet.FrameCount, sheet.Pixels);
            paths.Add(SheetPath(recipe, clip));
        }

        string setPath = SetPath(recipe);
        WriteSet(setPath, BuildSet(recipe, baker));
        paths.Add(setPath);
        return paths;
    }

    /// <summary>
    /// Writes one clip's sheet, <paramref name="pixels"/> (rows from the bottom) of <paramref name="columns"/> frames of
    /// <paramref name="frameSize"/> a row and a row per direction, at <see cref="SheetPath(string, string)"/> when its
    /// bytes changed, imports it at <see cref="PixelsPerUnit"/> and slices it into
    /// <see cref="SliceName(string, string, int, int)"/> at <see cref="CharacterSheet.FrameRect"/> with the pivot on the
    /// <paramref name="feet"/> pixel, keeping existing slices' IDs. A sheet already imported gets its new slices before
    /// its new pixels import, so a sheet that lost frames never imports with slices past its edge. Returns whether the
    /// file was written or its import settings or slices changed; an unchanged sheet is neither rewritten nor
    /// reimported.
    /// </summary>
    public static bool WriteSheet(string sheetName, string clipName, Vector2Int frameSize, Vector2Int feet, int columns, Color32[] pixels)
    {
        TerrainMaterialWriter.EnsureFolder(FolderOf(sheetName));
        string path = SheetPath(sheetName, clipName);
        int width = columns * frameSize.x;
        int height = CharacterDirections.Count * frameSize.y;
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
        bool changed = ApplyImportSettings(importer);
        changed |= FitMaxSize(importer, Mathf.Max(width, height));
        changed |= Slice(importer, sheetName, clipName, frameSize, feet, columns);
        if (written || changed)
        {
            importer.SaveAndReimport();
        }

        return written || changed;
    }

    /// <summary>
    /// The sprites of a written sheet of <paramref name="columns"/> frames a direction, direction by direction and
    /// within each frame by frame, found by their slice names (null where a slice is missing).
    /// </summary>
    public static Sprite[] SheetSprites(string sheetName, string clipName, int columns)
    {
        Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath(sheetName, clipName)).OfType<Sprite>().ToDictionary(s => s.name);
        Sprite[] ordered = new Sprite[CharacterDirections.Count * columns];
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int column = 0; column < columns; column++)
            {
                sprites.TryGetValue(SliceName(sheetName, clipName, direction, column), out ordered[direction * columns + column]);
            }
        }

        return ordered;
    }

    /// <summary>
    /// The path of <paramref name="recipe"/>'s animation set.
    /// </summary>
    public static string SetPath(CharacterRecipe recipe)
    {
        return SetPath(recipe.SheetName);
    }

    /// <summary>
    /// The path of the animation set of the character whose sheets are named <paramref name="sheetName"/>.
    /// </summary>
    public static string SetPath(string sheetName)
    {
        return $"{FolderOf(sheetName)}/{sheetName}_AnimationSet.asset";
    }

    /// <summary>
    /// How fast <paramref name="clip"/>'s planted feet travel at its natural speed, in units per second: its motion's
    /// ground speed at the baker's pixels per rig unit, over <see cref="PixelsPerUnit"/>. This holds facing east or
    /// west; facing north or south the view's pitch shows the stride at half its length.
    /// </summary>
    public static float NaturalSpeed(CharacterBaker baker, CharacterRecipe recipe, CharacterClip clip)
    {
        return clip.Motion.GroundSpeed(recipe) * baker.Scale / PixelsPerUnit;
    }

    /// <summary>
    /// An unsaved set of <paramref name="recipe"/>'s clips over the sprites of its written sheets, without touching an
    /// asset.
    /// </summary>
    public static CharacterAnimationSet BuildSet(CharacterRecipe recipe, CharacterBaker baker)
    {
        List<CharacterAnimationClip> clips = new();
        foreach (CharacterClip clip in recipe.Clips)
        {
            int columns = clip.Motion.FrameCount;
            int gaitFrames = clip.Motion.GaitFrames;
            int frames = columns / Mathf.Max(1, gaitFrames);
            Sprite[] ordered = SheetSprites(recipe.SheetName, clip.Name, columns);
            float[] seconds = Enumerable.Range(0, frames).Select(clip.Motion.FrameSeconds).ToArray();
            clips.Add(CharacterAnimationClip.Create(clip.Name, clip.Motion.Gait, clip.Weapon != null, ordered, seconds, NaturalSpeed(baker, recipe, clip), clip.Motion.ContactFrame, gaitFrames));
        }

        return CharacterAnimationSet.Create(clips);
    }

    /// <summary>
    /// Writes the unsaved set <paramref name="fresh"/> at <paramref name="path"/>: creates the asset from it, or copies
    /// it over the existing asset when they differ, keeping its GUID, and destroys it. Returns whether the asset was
    /// created or changed.
    /// </summary>
    public static bool WriteSet(string path, CharacterAnimationSet fresh)
    {
        string setName = Path.GetFileNameWithoutExtension(path);
        fresh.name = setName;
        CharacterAnimationSet existing = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return true;
        }

        bool changed = EditorJsonUtility.ToJson(existing) != EditorJsonUtility.ToJson(fresh);
        if (changed)
        {
            EditorUtility.CopySerialized(fresh, existing);
            existing.name = setName;
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
        }

        Object.DestroyImmediate(fresh);
        return changed;
    }

    /// <summary>
    /// The folder of <paramref name="recipe"/>'s sheets.
    /// </summary>
    public static string FolderOf(CharacterRecipe recipe)
    {
        return FolderOf(recipe.SheetName);
    }

    /// <summary>
    /// The folder of the sheets named <paramref name="sheetName"/>.
    /// </summary>
    public static string FolderOf(string sheetName)
    {
        return $"{Root}/{sheetName}";
    }

    /// <summary>
    /// The path of <paramref name="clip"/>'s sheet.
    /// </summary>
    public static string SheetPath(CharacterRecipe recipe, CharacterClip clip)
    {
        return SheetPath(recipe.SheetName, clip.Name);
    }

    /// <summary>
    /// The path of clip <paramref name="clipName"/>'s sheet among the sheets named <paramref name="sheetName"/>.
    /// </summary>
    public static string SheetPath(string sheetName, string clipName)
    {
        return $"{FolderOf(sheetName)}/{sheetName}_{clipName}.png";
    }

    /// <summary>
    /// The name of the slice of frame <paramref name="frame"/> of direction <paramref name="direction"/> of
    /// <paramref name="clip"/>.
    /// </summary>
    public static string SliceName(CharacterRecipe recipe, CharacterClip clip, int direction, int frame)
    {
        return SliceName(recipe.SheetName, clip.Name, direction, frame);
    }

    /// <summary>
    /// The name of the slice of frame <paramref name="frame"/> of direction <paramref name="direction"/> of clip
    /// <paramref name="clipName"/> among the sheets named <paramref name="sheetName"/>.
    /// </summary>
    public static string SliceName(string sheetName, string clipName, int direction, int frame)
    {
        return $"{sheetName}_{clipName}_{CharacterDirections.Name(direction)}_{frame}";
    }

    /// <summary>
    /// Sets a sheet's import settings (a point-filtered, uncompressed sprite sheet without mipmaps at
    /// <see cref="PixelsPerUnit"/>), without reimporting, and returns whether any changed.
    /// </summary>
    public static bool ApplyImportSettings(TextureImporter importer)
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
    /// Gives the sheet one slice per direction and frame, named <see cref="SliceName(string, string, int, int)"/>, at its
    /// frame's rect with a custom pivot on the <paramref name="feet"/> pixel, through the sprite editor's data provider
    /// so existing slices keep their IDs, and removes any other slice, without reimporting. Returns whether any slice
    /// changed, was added or removed.
    /// </summary>
    private static bool Slice(TextureImporter importer, string sheetName, string clipName, Vector2Int frameSize, Vector2Int feet, int columns)
    {
        SpriteDataProviderFactories factories = new();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        List<SpriteRect> existing = provider.GetSpriteRects().ToList();
        ISpriteNameFileIdDataProvider fileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> pairs = fileIds.GetNameFileIdPairs().ToList();
        Vector2 pivot = new(feet.x / (float)frameSize.x, feet.y / (float)frameSize.y);
        List<SpriteRect> rects = new();
        bool changed = false;
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < columns; frame++)
            {
                string name = SliceName(sheetName, clipName, direction, frame);
                SpriteRect slice = existing.FirstOrDefault(r => r.name == name);
                if (slice == null)
                {
                    slice = new SpriteRect { name = name, spriteID = GUID.Generate() };
                    pairs.Add(new SpriteNameFileIdPair(name, slice.spriteID));
                    changed = true;
                }

                RectInt frameRect = CharacterSheet.FrameRect(direction, frame, frameSize);
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
