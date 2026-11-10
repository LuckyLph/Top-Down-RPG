using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks shared by the tests of the baked characters' recipes (the rogue's and the adventurer's): the project's sheets,
/// their palette maps and the animation set of a recipe are what it bakes.
/// </summary>
public static class CharacterSheetAssert
{
    /// <summary>
    /// Each of the project's sheets of <paramref name="recipe"/> is what it bakes (<paramref name="sheets"/>, by clip
    /// name), imported as sprites at 32 pixels per unit, sliced into one sprite per direction and frame named after the
    /// character, clip, direction and frame, at its frame's rect with its pivot on the clip's feet pixel. A sheet a player
    /// colours (Docs/PlayerCharacter.md, Phase 5) has its baked palette map beside it, imported as a single channel (R8),
    /// linear and point filtered, as its only secondary texture <c>_PaletteMap</c>; any other has neither.
    /// </summary>
    public static void SheetsAreWhatTheRecipeBakes(CharacterRecipe recipe, IReadOnlyDictionary<string, CharacterSheet> sheets)
    {
        foreach (CharacterClip clip in recipe.Clips)
        {
            string path = CharacterSheetWriter.SheetPath(recipe, clip);
            CharacterSheet sheet = sheets[clip.Name];
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(TerrainMaterialWriter.EncodePng(sheet.Width, sheet.Height, sheet.Pixels)), path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(CharacterSheetWriter.PixelsPerUnit), path);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple), path);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
            PaletteMapIsWhatTheRecipeBakes(recipe, clip, sheet, importer);

            Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            Assert.That(sprites.Count, Is.EqualTo(CharacterDirections.Count * sheet.FrameCount), path);
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                for (int frame = 0; frame < sheet.FrameCount; frame++)
                {
                    string name = CharacterSheetWriter.SliceName(recipe, clip, direction, frame);
                    Assert.That(sprites.TryGetValue(name, out Sprite sprite), Is.True, name);
                    RectInt rect = CharacterSheet.FrameRect(direction, frame, clip.FrameSize);
                    Assert.That(sprite.rect, Is.EqualTo(new Rect(rect.x, rect.y, rect.width, rect.height)), name);
                    Assert.That(sprite.pivot, Is.EqualTo((Vector2)clip.Pivot), name);
                    Assert.That(sprite.pixelsPerUnit, Is.EqualTo(CharacterSheetWriter.PixelsPerUnit), name);
                }
            }
        }
    }

    /// <summary>
    /// The palette map of <paramref name="clip"/>'s sheet: its baked bytes as grey levels beside the sheet, imported as a
    /// single channel and the sheet's only secondary texture, or, for a sheet without one, no file and no such texture.
    /// </summary>
    private static void PaletteMapIsWhatTheRecipeBakes(CharacterRecipe recipe, CharacterClip clip, CharacterSheet sheet, TextureImporter importer)
    {
        string mapPath = CharacterSheetWriter.PaletteMapPath(recipe.SheetName, clip.Name);
        SecondarySpriteTexture[] secondaries = importer.secondarySpriteTextures ?? new SecondarySpriteTexture[0];
        if (sheet.PaletteMap == null)
        {
            Assert.That(File.Exists(mapPath), Is.False, mapPath);
            Assert.That(secondaries.Any(secondary => secondary.name == CharacterPaletteMap.MapTextureName), Is.False, clip.Name);
            return;
        }

        Color32[] grey = sheet.PaletteMap.Select(code => new Color32(code, code, code, 255)).ToArray();
        Assert.That(File.ReadAllBytes(mapPath), Is.EqualTo(TerrainMaterialWriter.EncodePng(sheet.Width, sheet.Height, grey)), mapPath);
        Texture2D map = AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath);
        Assert.That(map.format, Is.EqualTo(TextureFormat.R8), mapPath);
        Assert.That(map.filterMode, Is.EqualTo(FilterMode.Point), mapPath);
        Assert.That(map.isDataSRGB, Is.False, mapPath);
        Assert.That(map.mipmapCount, Is.EqualTo(1), mapPath);
        Assert.That(secondaries.Length, Is.EqualTo(1), clip.Name);
        Assert.That(secondaries[0].name, Is.EqualTo(CharacterPaletteMap.MapTextureName), clip.Name);
        Assert.That(secondaries[0].texture, Is.EqualTo(map), clip.Name);
    }

    /// <summary>
    /// The project's animation set of <paramref name="recipe"/> holds every clip of it in order: its name, its motion's
    /// gait, whether it is drawn with a weapon, its motion's frame durations, its natural speed as
    /// <paramref name="baker"/> measures it, its motion's contact (release) frame (none for a gait), the gait frames it is
    /// drawn over (none but for a cast over the run), and for every direction, gait frame and frame the sheet's slice of
    /// that column. Its locomotion is the unarmed Idle, Walk, Run and Sprint. A recipe whose colours are its own names no
    /// dyes.
    /// </summary>
    public static void SetHoldsWhatTheRecipeBakes(CharacterRecipe recipe, CharacterBaker baker)
    {
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(recipe));
        Assert.That(set, Is.Not.Null);
        if (!recipe.HasDyes)
        {
            Assert.That(set.Dyes, Is.Zero);
        }

        Assert.That(set.Clips.Count, Is.EqualTo(recipe.Clips.Count));
        for (int i = 0; i < recipe.Clips.Count; i++)
        {
            CharacterClip clip = recipe.Clips[i];
            CharacterAnimationClip baked = set.Clips[i];
            Assert.That(baked.Name, Is.EqualTo(clip.Name));
            Assert.That(baked.Gait, Is.EqualTo(clip.Motion.Gait), clip.Name);
            Assert.That(baked.Armed, Is.EqualTo(clip.Weapon != null), clip.Name);
            Assert.That(baked.IsComplete, Is.True, clip.Name);
            int gaitFrames = clip.Motion.GaitFrames;
            int frames = clip.Motion.FrameCount / Mathf.Max(1, gaitFrames);
            Assert.That(baked.GaitFrames, Is.EqualTo(gaitFrames), clip.Name);
            Assert.That(baked.FrameCount, Is.EqualTo(frames), clip.Name);
            Assert.That(baked.NaturalSpeed, Is.EqualTo(CharacterSheetWriter.NaturalSpeed(baker, recipe, clip)).Within(1e-5f), clip.Name);
            Assert.That(baked.ContactFrame, Is.EqualTo(clip.Motion.ContactFrame), clip.Name);
            Assert.That(baked.HasContact, Is.EqualTo(clip.Motion.ContactFrame >= 0), clip.Name);
            string sheetPath = CharacterSheetWriter.SheetPath(recipe, clip);
            for (int frame = 0; frame < frames; frame++)
            {
                Assert.That(baked.FrameSeconds(frame), Is.EqualTo(clip.Motion.FrameSeconds(frame)), $"{clip.Name} {frame}");
                for (int direction = 0; direction < CharacterDirections.Count; direction++)
                {
                    for (int gaitFrame = 0; gaitFrame < Mathf.Max(1, gaitFrames); gaitFrame++)
                    {
                        Sprite sprite = baked.SpriteAt(direction, frame, gaitFrame);
                        Assert.That(sprite, Is.Not.Null, $"{clip.Name} {direction} {gaitFrame} {frame}");
                        Assert.That(sprite.name, Is.EqualTo(CharacterSheetWriter.SliceName(recipe, clip, direction, gaitFrame * frames + frame)));
                        Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(sheetPath));
                    }
                }
            }
        }

        Assert.That(set.Locomotion(CharacterGait.Idle).Name, Is.EqualTo("Idle"));
        Assert.That(set.Locomotion(CharacterGait.Walk).Name, Is.EqualTo("Walk"));
        Assert.That(set.Locomotion(CharacterGait.Run).Name, Is.EqualTo("Run"));
        Assert.That(set.Locomotion(CharacterGait.Sprint).Name, Is.EqualTo("Sprint"));
    }
}
