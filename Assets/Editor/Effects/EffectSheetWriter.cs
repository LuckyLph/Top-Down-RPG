using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Bakes and writes an <see cref="EffectRecipe"/>: its frames as one sheet,
/// <c>Assets/Sprites/Effects/&lt;sheet name&gt;.png</c>, a row per variant and direction (variant by variant, the first
/// at the top) and a column per frame, sliced into <c>&lt;sheet name&gt;_&lt;variant&gt;_&lt;direction&gt;_&lt;frame&gt;</c>
/// pivoted at the recipe's pivot, and the runtime <see cref="EffectSprites"/>
/// <c>&lt;sheet name&gt;_Sprites.asset</c> beside it. Baking touches no asset and gives the same pixels for the same
/// recipe; writing skips a sheet whose bytes did not change, and the sprite set keeps its GUID.
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

        TerrainMaterialWriter.EnsureFolder(Folder);
        string sheetPath = SheetPath(recipe);
        int rows = recipe.VariantCount * recipe.DirectionCount;
        List<SpriteSheetFiles.Slice> slices = new();
        for (int variant = 0; variant < recipe.VariantCount; variant++)
        {
            for (int direction = 0; direction < recipe.DirectionCount; direction++)
            {
                int bottom = (rows - 1 - (variant * recipe.DirectionCount + direction)) * recipe.FrameHeight;
                for (int frame = 0; frame < recipe.FrameCount; frame++)
                {
                    Rect rect = new(frame * recipe.FrameWidth, bottom, recipe.FrameWidth, recipe.FrameHeight);
                    slices.Add(new SpriteSheetFiles.Slice(SliceName(recipe, variant, direction, frame), rect, recipe.Pivot));
                }
            }
        }

        SpriteSheetFiles.WriteSheet(sheetPath, recipe.FrameWidth * recipe.FrameCount, recipe.FrameHeight * rows, Bake(recipe), slices);
        return new[] { sheetPath, WriteSprites(recipe, sheetPath) };
    }

    /// <summary>
    /// The path of the recipe's sheet.
    /// </summary>
    public static string SheetPath(EffectRecipe recipe)
    {
        return $"{Folder}/{recipe.SheetName}.png";
    }

    /// <summary>
    /// The path of the recipe's sprite set.
    /// </summary>
    public static string SpritesPath(EffectRecipe recipe)
    {
        return $"{Folder}/{recipe.SheetName}_Sprites.asset";
    }

    /// <summary>
    /// The name of the slice of frame <paramref name="frame"/> of <paramref name="variant"/> facing
    /// <paramref name="direction"/>.
    /// </summary>
    public static string SliceName(EffectRecipe recipe, int variant, int direction, int frame)
    {
        return $"{recipe.SheetName}_{variant}_{direction}_{frame}";
    }

    /// <summary>
    /// Writes the recipe's sprite set over the sliced sheet at <paramref name="sheetPath"/>: creates it, or copies the
    /// new set over it when it differs, keeping its GUID. Returns its path.
    /// </summary>
    private static string WriteSprites(EffectRecipe recipe, string sheetPath)
    {
        Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        Sprite[] ordered = new Sprite[recipe.VariantCount * recipe.DirectionCount * recipe.FrameCount];
        for (int variant = 0; variant < recipe.VariantCount; variant++)
        {
            for (int direction = 0; direction < recipe.DirectionCount; direction++)
            {
                for (int frame = 0; frame < recipe.FrameCount; frame++)
                {
                    int index = (variant * recipe.DirectionCount + direction) * recipe.FrameCount + frame;
                    sprites.TryGetValue(SliceName(recipe, variant, direction, frame), out ordered[index]);
                }
            }
        }

        string path = SpritesPath(recipe);
        string setName = Path.GetFileNameWithoutExtension(path);
        EffectSprites fresh = EffectSprites.Create(ordered, recipe.VariantCount, recipe.DirectionCount, recipe.FrameCount, recipe.FrameSeconds);
        fresh.name = setName;
        EffectSprites current = AssetDatabase.LoadAssetAtPath<EffectSprites>(path);
        if (current == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return path;
        }

        if (EditorJsonUtility.ToJson(current) != EditorJsonUtility.ToJson(fresh))
        {
            EditorUtility.CopySerialized(fresh, current);
            current.name = setName;
            EditorUtility.SetDirty(current);
            AssetDatabase.SaveAssetIfDirty(current);
        }

        Object.DestroyImmediate(fresh);
        return path;
    }
}
