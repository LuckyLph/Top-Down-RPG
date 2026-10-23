using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Writes what an <see cref="AsepriteEffectRecipe"/> draws (Docs/AsepriteCharacters.md, Phase 3): each variant's file
/// read and checked by <see cref="AsepriteClipReader.ReadEffect"/>, then checked against the first (the same frame size,
/// frame count and frame duration, which <see cref="EffectSprites"/> holds once for the whole effect), its frames copied
/// into one sheet laid out as the baked effects' (<see cref="EffectSheetWriter.Bake"/>: a row per variant and direction,
/// the first at the top, a column per frame) and written with its <see cref="EffectSprites"/> through
/// <see cref="EffectSheetWriter.Write"/>, the slices pivoted on the recipe's pixel and keeping their IDs by name, the
/// set its GUID. An effect plays every variant, so a problem in any file writes nothing; an unchanged sheet or set is
/// not rewritten.
/// </summary>
public static class AsepriteEffectWriter
{
    /// <summary>
    /// Reads every variant's file of <paramref name="recipe"/> and writes the sheet and its sprite set. Writes nothing
    /// when the recipe cannot generate (<see cref="AsepriteEffectRecipe.CanGenerate"/>) or a file has a problem,
    /// reporting why.
    /// </summary>
    public static AsepriteReport Generate(AsepriteEffectRecipe recipe)
    {
        AsepriteReport report = new();
        if (!recipe.CanGenerate(out string problem))
        {
            report.Problems.Add($"{recipe.name}: {problem}");
            return report;
        }

        List<string> paths = new();
        for (int variant = 0; variant < recipe.VariantCount; variant++)
        {
            paths.Add(recipe.FilePathOf(variant));
        }

        Color32[] pixels = Read(paths, recipe.Directional, recipe.Pivot, report.Problems, out AsepriteClipFrames first);
        if (pixels == null)
        {
            return report;
        }

        Vector2 pivot = SpriteSheetFiles.Slice.PivotOn(recipe.Pivot, first.FrameSize);
        report.Written.AddRange(EffectSheetWriter.Write(recipe.SheetName, first.FrameSize, pivot, paths.Count, first.DirectionCount, first.FrameCount, first.FrameSeconds[0], pixels));
        return report;
    }

    /// <summary>
    /// Reads the variants' files at <paramref name="paths"/> as an effect's, pivoted on <paramref name="pivot"/>, and
    /// gives the whole sheet (rows from the bottom), with <paramref name="first"/> the first variant read; or null after
    /// adding to <paramref name="problems"/> each problem of a file and each way a file differs from the first.
    /// </summary>
    internal static Color32[] Read(IReadOnlyList<string> paths, bool directional, Vector2Int pivot, List<string> problems, out AsepriteClipFrames first)
    {
        first = null;
        List<AsepriteClipFrames> variants = new();
        foreach (string path in paths)
        {
            AsepriteClipFrames frames = AsepriteClipReader.ReadEffect(path, directional, pivot);
            problems.AddRange(frames.Problems);
            variants.Add(frames);
        }

        if (variants.Exists(v => !v.IsValid))
        {
            return null;
        }

        first = variants[0];
        int found = problems.Count;
        string firstFile = Path.GetFileName(paths[0]);
        for (int i = 1; i < variants.Count; i++)
        {
            AsepriteClipFrames frames = variants[i];
            string file = Path.GetFileName(paths[i]);
            if (frames.FrameSize != first.FrameSize)
            {
                problems.Add($"{file}: a {frames.FrameSize.x} x {frames.FrameSize.y} canvas, but {first.FrameSize.x} x {first.FrameSize.y} in {firstFile}; every variant is the same size.");
            }

            if (frames.FrameCount != first.FrameCount)
            {
                problems.Add($"{file}: {frames.FrameCount} frames a direction, but {first.FrameCount} in {firstFile}; every variant has as many.");
            }

            if (frames.FrameSeconds[0] != first.FrameSeconds[0])
            {
                problems.Add($"{file}: frames of {Milliseconds(frames)} ms, but {Milliseconds(first)} ms in {firstFile}; every variant's frames show as long.");
            }
        }

        if (problems.Count > found)
        {
            return null;
        }

        int width = first.SheetWidth;
        int block = first.SheetHeight;
        Color32[] pixels = new Color32[width * block * variants.Count];
        for (int variant = 0; variant < variants.Count; variant++)
        {
            Array.Copy(variants[variant].Pixels, 0, pixels, (variants.Count - 1 - variant) * block * width, block * width);
        }

        return pixels;
    }

    /// <summary>
    /// The frame duration of <paramref name="frames"/> in whole milliseconds, as the file stores it.
    /// </summary>
    private static int Milliseconds(AsepriteClipFrames frames)
    {
        return Mathf.RoundToInt(frames.FrameSeconds[0] * 1000f);
    }
}
