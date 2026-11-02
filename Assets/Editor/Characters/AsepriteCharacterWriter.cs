using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Writes what an <see cref="AsepriteCharacterRecipe"/> draws (Docs/AsepriteCharacters.md, How it works): each clip's
/// file read and checked by <see cref="AsepriteClipReader"/>, its frames copied into a sheet laid out and sliced as the
/// baker's (<see cref="CharacterSheetWriter.WriteSheet"/>, the slices pivoted on the clip's feet pixel and keeping their
/// IDs by name), then the runtime <see cref="CharacterAnimationSet"/> beside the sheets
/// (<see cref="CharacterSheetWriter.WriteSet"/>, keeping its GUID): per clip its sprites by direction and frame, the
/// file's frame durations, its gait, whether it is armed, its contact (release) frame and a walk's, run's or sprint's
/// natural speed. A clip carrying a lantern also gets its lantern's layer written as the baker writes the rogue's
/// (<see cref="CharacterSheetWriter.WriteLantern(string, string, Vector2Int, Vector2Int, int, Color32[], Color32[])"/>:
/// <c>&lt;clip&gt;Lantern.png</c>, packed, with its glow) and, in the set, each sprite's lantern layer and the light it
/// casts; a clip without one loses any written before. A clip with a problem is reported and left out of the set; the
/// others are written. The clips the recipe's prefab plays and the set lacks are reported too
/// (<see cref="CharacterClipNeeds"/>). An unchanged sheet or set is not rewritten.
/// </summary>
public static class AsepriteCharacterWriter
{
    /// <summary>
    /// Reads and writes every clip of <paramref name="recipe"/>, then its set. Writes nothing when the recipe cannot
    /// generate (<see cref="AsepriteCharacterRecipe.CanGenerate"/>), reporting why.
    /// </summary>
    public static AsepriteReport Generate(AsepriteCharacterRecipe recipe)
    {
        AsepriteReport report = new();
        if (!recipe.CanGenerate(out string problem))
        {
            report.Problems.Add($"{recipe.name}: {problem}");
            return report;
        }

        string character = recipe.CharacterName;
        List<CharacterAnimationClip> clips = new();
        foreach (AsepriteClip clip in recipe.Clips)
        {
            string name = recipe.ClipName(clip);
            AsepriteClipFrames frames = AsepriteClipReader.Read(clip.FilePath, clip.Gait, clip.Feet);
            if (!frames.IsValid)
            {
                report.Problems.AddRange(frames.Problems);
                continue;
            }

            if (CharacterSheetWriter.WriteSheet(character, name, frames.FrameSize, clip.Feet, frames.FrameCount, frames.Pixels))
            {
                report.Written.Add(CharacterSheetWriter.SheetPath(character, name));
            }

            string lanternName = name + CharacterSheetWriter.LanternSuffix;
            if (CharacterSheetWriter.WriteLantern(character, name, frames.FrameSize, clip.Feet, frames.FrameCount, frames.LanternPixels, frames.LanternGlow))
            {
                report.Written.Add(CharacterSheetWriter.SheetPath(character, lanternName));
            }

            Sprite[] sprites = CharacterSheetWriter.SheetSprites(character, name, frames.FrameCount);
            Sprite[] lantern = frames.HasLantern ? CharacterSheetWriter.SheetSprites(character, lanternName, frames.FrameCount) : null;
            clips.Add(CharacterAnimationClip.Create(name, clip.Gait, clip.Armed, sprites, frames.FrameSeconds, clip.Moves ? clip.NaturalSpeed : 0f, frames.ContactFrame,
                clipLanternSprites: lantern, clipLanternLights: CharacterSheetWriter.LanternLights(frames.LanternCentres)));
        }

        string setPath = CharacterSheetWriter.SetPath(character);
        if (CharacterSheetWriter.WriteSet(setPath, CharacterAnimationSet.Create(clips)))
        {
            report.Written.Add(setPath);
        }

        if (recipe.Character != null)
        {
            report.Problems.AddRange(CharacterClipNeeds.Missing(recipe.Character, clips.Select(c => c.Name)));
        }

        return report;
    }
}
