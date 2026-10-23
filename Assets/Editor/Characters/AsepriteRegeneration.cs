using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Regenerates the characters drawn by hand whose files were saved (Docs/AsepriteCharacters.md, Phase 2): after an
/// import of <c>.aseprite</c> files under <see cref="AsepriteCharacterRecipe.ArtRoot"/>, every
/// <see cref="AsepriteCharacterRecipe"/> asset using one of them is queued, once however many of its files came in, and
/// generated on the editor's next update, once the import is over (<see cref="AsepriteCharacterWriter"/>, which
/// rewrites only the sheets and the set that changed); other recipes are left alone. What was written is logged, and
/// the problems found as a warning on the recipe. Unity calls asset postprocessors statically, so the queue is static.
/// </summary>
public sealed class AsepriteRegeneration : AssetPostprocessor
{
    private static readonly HashSet<string> Pending = new();
    private static bool scheduled;

    /// <summary>
    /// The recipes queued for regeneration, by asset path.
    /// </summary>
    internal static IReadOnlyCollection<string> Queued => Pending;

    /// <summary>
    /// Queues the recipes using the character files among <paramref name="importedAssets"/>.
    /// </summary>
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        List<string> files = importedAssets.Where(p => p.EndsWith(AsepriteCharacterRecipe.Extension, StringComparison.OrdinalIgnoreCase) && AsepriteImportSettings.IsCharacterArt(p)).ToList();
        if (files.Count > 0)
        {
            Queue(files);
        }
    }

    /// <summary>
    /// Queues every recipe asset with a clip whose file is one of <paramref name="files"/>, and schedules the
    /// regeneration for the editor's next update.
    /// </summary>
    internal static void Queue(IReadOnlyCollection<string> files)
    {
        HashSet<string> saved = new(files);
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(AsepriteCharacterRecipe)}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AsepriteCharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<AsepriteCharacterRecipe>(path);
            if (recipe != null && recipe.Clips.Any(c => c != null && c.FilePath != null && saved.Contains(c.FilePath)))
            {
                Pending.Add(path);
            }
        }

        if (Pending.Count > 0 && !scheduled)
        {
            scheduled = true;
            EditorApplication.delayCall += () => Flush();
        }
    }

    /// <summary>
    /// Generates every queued recipe still there and empties the queue; returns their paths.
    /// </summary>
    internal static IReadOnlyList<string> Flush()
    {
        scheduled = false;
        List<string> paths = Pending.OrderBy(p => p, StringComparer.Ordinal).ToList();
        Pending.Clear();
        List<string> generated = new();
        foreach (string path in paths)
        {
            AsepriteCharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<AsepriteCharacterRecipe>(path);
            if (recipe == null)
            {
                continue;
            }

            AsepriteCharacterReport report = AsepriteCharacterWriter.Generate(recipe);
            generated.Add(path);
            if (report.Written.Count > 0)
            {
                Debug.Log($"Regenerated {recipe.name} after its files were saved: {string.Join(", ", report.Written)}.", recipe);
            }

            if (report.Problems.Count > 0)
            {
                Debug.LogWarning($"{recipe.name} has {report.Problems.Count} problems:\n{string.Join("\n", report.Problems)}", recipe);
            }
        }

        return generated;
    }
}
