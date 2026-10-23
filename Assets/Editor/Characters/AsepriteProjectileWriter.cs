using System.IO;
using UnityEngine;

/// <summary>
/// Writes what an <see cref="AsepriteProjectileRecipe"/> draws (Docs/AsepriteCharacters.md, Phase 3): its file read and
/// checked by <see cref="AsepriteClipReader.ReadDirections"/>, one frame per direction counterclockwise from east, its
/// frame count checked against the recipe's directions, then written as one row of frames with its
/// <see cref="DirectionalSprites"/> through <see cref="WeaponSpriteWriter.WriteDirections"/>, as the bow's baked arrow is:
/// the slices pivoted on the tip and keeping their IDs by name, the set its GUID. A file with a problem writes nothing;
/// an unchanged sheet or set is not rewritten.
/// </summary>
public static class AsepriteProjectileWriter
{
    /// <summary>
    /// Reads the file of <paramref name="recipe"/> and writes the sheet and its direction set. Writes nothing when the
    /// recipe cannot generate (<see cref="AsepriteProjectileRecipe.CanGenerate"/>) or the file has a problem, reporting
    /// why.
    /// </summary>
    public static AsepriteReport Generate(AsepriteProjectileRecipe recipe)
    {
        AsepriteReport report = new();
        if (!recipe.CanGenerate(out string problem))
        {
            report.Problems.Add($"{recipe.name}: {problem}");
            return report;
        }

        AsepriteClipFrames frames = Read(recipe.FilePath, recipe.Directions, recipe.Tip, report);
        if (frames == null)
        {
            return report;
        }

        Vector2 pivot = SpriteSheetFiles.Slice.PivotOn(recipe.Tip, frames.FrameSize);
        report.Written.AddRange(WeaponSpriteWriter.WriteDirections(recipe.SheetName, frames.FrameSize, pivot, recipe.Directions, frames.Pixels));
        return report;
    }

    /// <summary>
    /// Reads the file at <paramref name="path"/> as a projectile drawn in <paramref name="directions"/> directions
    /// pivoted on <paramref name="tip"/>, or gives null after adding its problems to <paramref name="report"/>: those of
    /// the file, and a frame count other than the directions'.
    /// </summary>
    internal static AsepriteClipFrames Read(string path, int directions, Vector2Int tip, AsepriteReport report)
    {
        AsepriteClipFrames frames = AsepriteClipReader.ReadDirections(path, tip);
        report.Problems.AddRange(frames.Problems);
        if (!frames.IsValid)
        {
            return null;
        }

        if (frames.FrameCount != directions)
        {
            report.Problems.Add($"{Path.GetFileName(path)}: {frames.FrameCount} frames, but the projectile is drawn in {directions} directions, a frame each: frame 1 faces east and each next one {360f / directions:0.##} degrees further counterclockwise.");
            return null;
        }

        return frames;
    }
}
