using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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
/// by gait frame), each frame's duration at natural speed, its gait, whether it is armed, its contact (release) frame,
/// a gait's natural speed (<see cref="NaturalSpeed"/>), for a cast with a light, the light each sprite casts and
/// its colour (<see cref="Lights"/>), and for a character carrying a lantern, each sprite's lantern layer and the light
/// the lantern casts (<see cref="LanternLights"/>); the asset keeps its GUID and is saved only when it changed. The
/// lantern's layer of each clip is a sheet of its own, <c>&lt;sheet name&gt;_&lt;clip&gt;Lantern.png</c>, packed
/// tightly (<see cref="PackLantern"/>), with its glow beside it in <see cref="GlowRoot"/> as its secondary texture
/// <c>_GlowTex</c>; a recipe without a lantern has neither. <see cref="AsepriteCharacterWriter"/> writes a hand-drawn
/// clip's lantern layer the same way.
/// A sheet larger than its import's maximum size (a cast drawn over the run's eight frames) gets the size it needs.
/// Writing a sheet (<see cref="WriteSheet"/>) and a set (<see cref="WriteSet"/>) goes through
/// <see cref="SpriteSheetFiles"/> and is shared with <see cref="AsepriteCharacterWriter"/>, which writes hand-drawn
/// clips the same way.
/// </summary>
public static class CharacterSheetWriter
{
    public const string Root = "Assets/Sprites/Characters";
    public const string GlowRoot = "Assets/Textures/Characters";
    public const string LanternSuffix = "Lantern";
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
            WriteLantern(recipe.SheetName, sheet);
            if (sheet.HasLantern)
            {
                paths.Add(SheetPath(recipe.SheetName, clip.Name + LanternSuffix));
            }
        }

        string setPath = SetPath(recipe);
        WriteSet(setPath, BuildSet(recipe, baker, sheets));
        paths.Add(setPath);
        return paths;
    }

    /// <summary>
    /// Writes one clip's sheet, <paramref name="pixels"/> (rows from the bottom) of <paramref name="columns"/> frames of
    /// <paramref name="frameSize"/> a row and a row per direction, at <see cref="SheetPath(string, string)"/> through
    /// <see cref="SpriteSheetFiles.WriteSheet"/>, sliced into <see cref="SliceName(string, string, int, int)"/> at
    /// <see cref="CharacterSheet.FrameRect"/> with the pivot on the <paramref name="feet"/> pixel, keeping existing
    /// slices' IDs and removing the others. Returns whether the file was written or its import settings or slices
    /// changed; an unchanged sheet is neither rewritten nor reimported.
    /// </summary>
    public static bool WriteSheet(string sheetName, string clipName, Vector2Int frameSize, Vector2Int feet, int columns, Color32[] pixels)
    {
        TerrainMaterialWriter.EnsureFolder(FolderOf(sheetName));
        Vector2 pivot = SpriteSheetFiles.Slice.PivotOn(feet, frameSize);
        List<SpriteSheetFiles.Slice> slices = new();
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < columns; frame++)
            {
                RectInt rect = CharacterSheet.FrameRect(direction, frame, frameSize);
                slices.Add(new SpriteSheetFiles.Slice(SliceName(sheetName, clipName, direction, frame), new Rect(rect.x, rect.y, rect.width, rect.height), pivot));
            }
        }

        return SpriteSheetFiles.WriteSheet(SheetPath(sheetName, clipName), columns * frameSize.x, CharacterDirections.Count * frameSize.y, pixels, slices);
    }

    /// <summary>
    /// Writes <paramref name="sheet"/>'s lantern layer as the sheet <c>&lt;clip&gt;Lantern</c> among
    /// <paramref name="sheetName"/>'s (see <see cref="WriteLantern(string, string, Vector2Int, Vector2Int, int, Color32[], Color32[])"/>),
    /// or deletes the one written before for a sheet without a lantern. Returns whether anything was written.
    /// </summary>
    public static bool WriteLantern(string sheetName, CharacterSheet sheet)
    {
        CharacterClip clip = sheet.Clip;
        return WriteLantern(sheetName, clip.Name, clip.FrameSize, clip.Pivot, sheet.FrameCount, sheet.LanternPixels, sheet.LanternGlow);
    }

    /// <summary>
    /// Writes clip <paramref name="clipName"/>'s lantern layer, <paramref name="lanternPixels"/> laid out as its sheet
    /// (<paramref name="columns"/> frames of <paramref name="frameSize"/> a row, a row per direction, rows from the
    /// bottom) with its glowing pixels in <paramref name="lanternGlow"/>, packed (see
    /// <see cref="PackLantern(string, string, Vector2Int, Vector2Int, int, Color32[], Color32[], out Vector2Int, out Color32[], out Color32[])"/>)
    /// as the sheet <c>&lt;clip&gt;Lantern</c> among <paramref name="sheetName"/>'s, with its glow in
    /// <see cref="GlowRoot"/> as its secondary texture <c>_GlowTex</c>; without a layer (null), deletes any written
    /// before. Returns whether the sheet, its glow or their import settings were written; an unchanged layer writes
    /// nothing.
    /// </summary>
    public static bool WriteLantern(string sheetName, string clipName, Vector2Int frameSize, Vector2Int feet, int columns, Color32[] lanternPixels, Color32[] lanternGlow)
    {
        string lanternName = clipName + LanternSuffix;
        string path = SheetPath(sheetName, lanternName);
        string glowPath = GlowPath(sheetName, lanternName);
        if (lanternPixels == null)
        {
            foreach (string stale in new[] { path, glowPath })
            {
                if (AssetDatabase.LoadMainAssetAtPath(stale) != null)
                {
                    AssetDatabase.DeleteAsset(stale);
                }
            }

            return false;
        }

        IReadOnlyList<SpriteSheetFiles.Slice> slices = PackLantern(sheetName, clipName, frameSize, feet, columns, lanternPixels, lanternGlow, out Vector2Int size, out Color32[] pixels, out Color32[] glow);
        TerrainMaterialWriter.EnsureFolder(FolderOf(sheetName));
        bool written = SpriteSheetFiles.WriteSheet(path, size.x, size.y, pixels, slices);
        written |= SpriteSheetFiles.WriteGlow(glowPath, size.x, size.y, glow);
        written |= SpriteSheetFiles.SetSecondaryTextures(path, new[] { new SecondarySpriteTexture { name = SpriteSheetFiles.GlowTextureName, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(glowPath) } });
        return written;
    }

    /// <summary>
    /// <paramref name="sheet"/>'s lantern layer packed tightly (see
    /// <see cref="PackLantern(string, string, Vector2Int, Vector2Int, int, Color32[], Color32[], out Vector2Int, out Color32[], out Color32[])"/>).
    /// </summary>
    public static IReadOnlyList<SpriteSheetFiles.Slice> PackLantern(string sheetName, CharacterSheet sheet, out Vector2Int size, out Color32[] pixels, out Color32[] glow)
    {
        CharacterClip clip = sheet.Clip;
        return PackLantern(sheetName, clip.Name, clip.FrameSize, clip.Pivot, sheet.FrameCount, sheet.LanternPixels, sheet.LanternGlow, out size, out pixels, out glow);
    }

    /// <summary>
    /// Clip <paramref name="clipName"/>'s lantern layer, <paramref name="lanternPixels"/> laid out as its sheet, packed
    /// tightly: each frame's pixels cut to the box around them and laid in a cell of the clip's largest box, the cells
    /// in the sheet's own layout (a row per direction, east at the top), and its glow, <paramref name="lanternGlow"/>,
    /// laid out alike. Each slice, named as the sheet's own frames under the clip <c>&lt;clip&gt;Lantern</c>, covers its
    /// frame's box and is pivoted on the <paramref name="feet"/>, outside the box as they usually are, so drawn at the
    /// character's feet it lands where the lantern was drawn. A frame where the body hides the whole lantern has no
    /// slice.
    /// </summary>
    public static IReadOnlyList<SpriteSheetFiles.Slice> PackLantern(string sheetName, string clipName, Vector2Int frameSize, Vector2Int feet, int columns, Color32[] lanternPixels, Color32[] lanternGlow,
        out Vector2Int size, out Color32[] pixels, out Color32[] glow)
    {
        int width = columns * frameSize.x;
        RectInt[] boxes = new RectInt[CharacterDirections.Count * columns];
        Vector2Int cell = Vector2Int.zero;
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int column = 0; column < columns; column++)
            {
                RectInt box = LanternBox(lanternPixels, width, CharacterSheet.FrameRect(direction, column, frameSize));
                boxes[direction * columns + column] = box;
                cell = Vector2Int.Max(cell, box.size);
            }
        }

        size = new Vector2Int(Mathf.Max(1, columns * cell.x), Mathf.Max(1, CharacterDirections.Count * cell.y));
        pixels = new Color32[size.x * size.y];
        glow = new Color32[pixels.Length];
        List<SpriteSheetFiles.Slice> slices = new();
        string lanternName = clipName + LanternSuffix;
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int column = 0; column < columns; column++)
            {
                RectInt box = boxes[direction * columns + column];
                if (box.width == 0)
                {
                    continue;
                }

                RectInt frame = CharacterSheet.FrameRect(direction, column, frameSize);
                RectInt target = CharacterSheet.FrameRect(direction, column, cell);
                for (int y = 0; y < box.height; y++)
                {
                    int from = (frame.y + box.y + y) * width + frame.x + box.x;
                    int to = (target.y + y) * size.x + target.x;
                    System.Array.Copy(lanternPixels, from, pixels, to, box.width);
                    System.Array.Copy(lanternGlow, from, glow, to, box.width);
                }

                Vector2 pivot = SpriteSheetFiles.Slice.PivotOn(feet - box.position, box.size);
                slices.Add(new SpriteSheetFiles.Slice(SliceName(sheetName, lanternName, direction, column), new Rect(target.x, target.y, box.width, box.height), pivot));
            }
        }

        return slices;
    }

    /// <summary>
    /// The sprites of a written sheet of <paramref name="columns"/> frames a direction, direction by direction and
    /// within each frame by frame, found by their slice names (null where a slice is missing).
    /// </summary>
    public static Sprite[] SheetSprites(string sheetName, string clipName, int columns)
    {
        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(SheetPath(sheetName, clipName));
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
    /// An unsaved set of <paramref name="recipe"/>'s clips, baked as <paramref name="sheets"/> (in the recipe's order),
    /// over the sprites of its written sheets, without touching an asset.
    /// </summary>
    public static CharacterAnimationSet BuildSet(CharacterRecipe recipe, CharacterBaker baker, IReadOnlyList<CharacterSheet> sheets)
    {
        List<CharacterAnimationClip> clips = new();
        foreach (CharacterSheet sheet in sheets)
        {
            CharacterClip clip = sheet.Clip;
            int columns = clip.Motion.FrameCount;
            int gaitFrames = clip.Motion.GaitFrames;
            int frames = columns / Mathf.Max(1, gaitFrames);
            Sprite[] ordered = SheetSprites(recipe.SheetName, clip.Name, columns);
            float[] seconds = Enumerable.Range(0, frames).Select(clip.Motion.FrameSeconds).ToArray();
            CharacterFrameLight[] lights = Lights(sheet, out Color lightColour);
            Sprite[] lantern = sheet.HasLantern ? SheetSprites(recipe.SheetName, clip.Name + LanternSuffix, columns) : null;
            clips.Add(CharacterAnimationClip.Create(clip.Name, clip.Motion.Gait, clip.Weapon != null, ordered, seconds, NaturalSpeed(baker, recipe, clip), clip.Motion.ContactFrame, gaitFrames, lightColour, lights,
                lantern, LanternLights(sheet.LanternCentres)));
        }

        return CharacterAnimationSet.Create(clips);
    }

    /// <summary>
    /// The light each of <paramref name="sheet"/>'s frames casts, in the order of <see cref="SheetSprites"/>: where its
    /// cast light shines from, in units from the feet, and how strongly; and in <paramref name="colour"/> the light's
    /// colour, the rim tone of its motion's light colours (<see cref="ColourRamp.Shadow"/>), which tints what it lights
    /// most (Docs/Lighting.md, Phase 2). Null and clear for a clip whose motion has no light colours or draws no light.
    /// </summary>
    public static CharacterFrameLight[] Lights(CharacterSheet sheet, out Color colour)
    {
        Color32 rim = sheet.Clip.Motion.Light.Shadow;
        colour = Color.clear;
        if (rim.a == 0 || sheet.LightStrengths.All(strength => strength <= 0f))
        {
            return null;
        }

        colour = rim;
        CharacterFrameLight[] lights = new CharacterFrameLight[sheet.LightStrengths.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i] = new CharacterFrameLight(sheet.LightCentres[i] / PixelsPerUnit, sheet.LightStrengths[i]);
        }

        return lights;
    }

    /// <summary>
    /// The light the lantern casts on each of <paramref name="sheet"/>'s frames (see
    /// <see cref="LanternLights(Vector2[])"/>), shining from the glass's middle. Null for a sheet without a lantern.
    /// </summary>
    public static CharacterFrameLight[] LanternLights(CharacterSheet sheet)
    {
        return LanternLights(sheet.LanternCentres);
    }

    /// <summary>
    /// The light a lantern casts on each frame, in the order of <see cref="SheetSprites"/>, from
    /// <paramref name="centres"/>, where it shines from on each in pixels from the feet: in units from the feet, at full
    /// strength, whether or not the body hides it (Docs/Lighting.md, Phase 9). Null without centres (no lantern).
    /// </summary>
    public static CharacterFrameLight[] LanternLights(Vector2[] centres)
    {
        if (centres == null)
        {
            return null;
        }

        CharacterFrameLight[] lights = new CharacterFrameLight[centres.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i] = new CharacterFrameLight(centres[i] / PixelsPerUnit, 1f);
        }

        return lights;
    }

    /// <summary>
    /// Writes the unsaved set <paramref name="fresh"/> at <paramref name="path"/>: creates the asset from it, or copies
    /// it over the existing asset when they differ, keeping its GUID, and destroys it. Returns whether the asset was
    /// created or changed.
    /// </summary>
    public static bool WriteSet(string path, CharacterAnimationSet fresh)
    {
        return SpriteSheetFiles.WriteAsset(path, fresh);
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
    /// The path of the glow beside sheet <paramref name="clipName"/> among the sheets named <paramref name="sheetName"/>.
    /// </summary>
    public static string GlowPath(string sheetName, string clipName)
    {
        return $"{GlowRoot}/{sheetName}/{sheetName}_{clipName}_Glow.png";
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
    /// The box, within <paramref name="frame"/> of a lantern layer <paramref name="width"/> pixels wide, around every
    /// pixel it draws, from the frame's bottom left; empty when it draws none.
    /// </summary>
    private static RectInt LanternBox(Color32[] lanternPixels, int width, RectInt frame)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < frame.height; y++)
        {
            for (int x = 0; x < frame.width; x++)
            {
                if (lanternPixels[(frame.y + y) * width + frame.x + x].a == 0)
                {
                    continue;
                }

                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }

        return maxX < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
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
}
