using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Bakes and writes an <see cref="EffectRecipe"/>: its frames as one sheet,
/// <c>Assets/Sprites/Effects/&lt;sheet name&gt;.png</c>, a row per variant and direction (variant by variant, the first
/// at the top) and a column per frame, sliced into <c>&lt;sheet name&gt;_&lt;variant&gt;_&lt;direction&gt;_&lt;frame&gt;</c>
/// pivoted at the recipe's pivot, and the runtime <see cref="EffectSprites"/>
/// <c>&lt;sheet name&gt;_Sprites.asset</c> beside it. Baking touches no asset and gives the same pixels for the same
/// recipe; writing (<see cref="Write"/>, shared with the effects drawn in Aseprite) goes through
/// <see cref="SpriteSheetFiles"/>, skips a sheet whose bytes did not change, keeps slice IDs by name, and the sprite set
/// keeps its GUID.
/// </summary>
public static class EffectSheetWriter
{
    public const string Folder = "Assets/Sprites/Effects";

    /// <summary>
    /// The whole sheet, rows from the bottom: <see cref="EffectRecipe.FrameCount"/> frames wide and a frame high for
    /// each variant and direction.
    /// </summary>
    public static Color32[] Bake(EffectRecipe recipe)
    {
        int frameWidth = recipe.FrameWidth;
        int frameHeight = recipe.FrameHeight;
        int width = frameWidth * recipe.FrameCount;
        int rows = recipe.VariantCount * recipe.DirectionCount;
        Color32[] pixels = new Color32[width * frameHeight * rows];
        for (int variant = 0; variant < recipe.VariantCount; variant++)
        {
            for (int direction = 0; direction < recipe.DirectionCount; direction++)
            {
                int bottom = (rows - 1 - (variant * recipe.DirectionCount + direction)) * frameHeight;
                for (int frame = 0; frame < recipe.FrameCount; frame++)
                {
                    Color32[] drawn = recipe.BakeFrame(variant, direction, frame);
                    for (int y = 0; y < frameHeight; y++)
                    {
                        Array.Copy(drawn, y * frameWidth, pixels, (bottom + y) * width + frame * frameWidth, frameWidth);
                    }
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// Writes the recipe's sheet and sprite set and returns their paths. Logs an error and writes nothing when the
    /// recipe cannot bake.
    /// </summary>
    public static IReadOnlyList<string> Generate(EffectRecipe recipe)
    {
        if (!recipe.CanGenerate(out string problem))
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            return null;
        }

        Write(recipe.SheetName, new Vector2Int(recipe.FrameWidth, recipe.FrameHeight), recipe.Pivot, recipe.VariantCount, recipe.DirectionCount, recipe.FrameCount, recipe.FrameSeconds, Bake(recipe));
        return new[] { SheetPath(recipe), SpritesPath(recipe) };
    }

    /// <summary>
    /// Writes an effect's sheet, <paramref name="pixels"/> (rows from the bottom, laid out as <see cref="Bake"/> lays
    /// them), at <see cref="SheetPath(string)"/> through <see cref="SpriteSheetFiles.WriteSheet"/>, sliced into
    /// <see cref="SliceName(string, int, int, int)"/> at <paramref name="pivot"/> (a share of the frame from its bottom
    /// left), then its <see cref="EffectSprites"/> at <see cref="SpritesPath(string)"/>, each frame shown
    /// <paramref name="frameSeconds"/>, keeping its GUID. Shared by the baked effect recipes and the effects drawn in
    /// Aseprite (<see cref="AsepriteEffectWriter"/>). Returns the paths it wrote: the sheet when it was written or its
    /// import settings or slices changed, the sprite set when it was created or changed.
    /// </summary>
    public static IReadOnlyList<string> Write(string sheetName, Vector2Int frameSize, Vector2 pivot, int variants, int directions, int frames, float frameSeconds, Color32[] pixels)
    {
        TerrainMaterialWriter.EnsureFolder(Folder);
        string sheetPath = SheetPath(sheetName);
        int rows = variants * directions;
        List<SpriteSheetFiles.Slice> slices = new();
        for (int variant = 0; variant < variants; variant++)
        {
            for (int direction = 0; direction < directions; direction++)
            {
                int bottom = (rows - 1 - (variant * directions + direction)) * frameSize.y;
                for (int frame = 0; frame < frames; frame++)
                {
                    Rect rect = new(frame * frameSize.x, bottom, frameSize.x, frameSize.y);
                    slices.Add(new SpriteSheetFiles.Slice(SliceName(sheetName, variant, direction, frame), rect, pivot));
                }
            }
        }

        List<string> written = new();
        if (SpriteSheetFiles.WriteSheet(sheetPath, frameSize.x * frames, frameSize.y * rows, pixels, slices))
        {
            written.Add(sheetPath);
        }

        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheetPath);
        Sprite[] ordered = slices.Select(s => sprites.TryGetValue(s.Name, out Sprite sprite) ? sprite : null).ToArray();
        string spritesPath = SpritesPath(sheetName);
        if (SpriteSheetFiles.WriteAsset(spritesPath, EffectSprites.Create(ordered, variants, directions, frames, frameSeconds)))
        {
            written.Add(spritesPath);
        }

        return written;
    }

    /// <summary>
    /// The path of the recipe's sheet.
    /// </summary>
    public static string SheetPath(EffectRecipe recipe)
    {
        return SheetPath(recipe.SheetName);
    }

    /// <summary>
    /// The path of the sheet named <paramref name="sheetName"/>.
    /// </summary>
    public static string SheetPath(string sheetName)
    {
        return $"{Folder}/{sheetName}.png";
    }

    /// <summary>
    /// The path of the recipe's sprite set.
    /// </summary>
    public static string SpritesPath(EffectRecipe recipe)
    {
        return SpritesPath(recipe.SheetName);
    }

    /// <summary>
    /// The path of the sprite set of the sheet named <paramref name="sheetName"/>.
    /// </summary>
    public static string SpritesPath(string sheetName)
    {
        return $"{Folder}/{sheetName}_Sprites.asset";
    }

    /// <summary>
    /// The name of the slice of frame <paramref name="frame"/> of <paramref name="variant"/> facing
    /// <paramref name="direction"/> on the sheet named <paramref name="sheetName"/>.
    /// </summary>
    public static string SliceName(string sheetName, int variant, int direction, int frame)
    {
        return $"{sheetName}_{variant}_{direction}_{frame}";
    }
}
