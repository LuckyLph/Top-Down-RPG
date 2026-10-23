using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Regenerates what was drawn by hand from files that were saved (Docs/AsepriteCharacters.md, Phases 2 and 3): after an
/// import of <c>.aseprite</c> files under <see cref="AsepriteImportSettings.ArtRoot"/>, every Aseprite recipe asset
/// (<see cref="RecipeTypes"/>: characters, effects and projectiles) using one of them is queued, once however many of
/// its files came in, and generated on the editor's next update, once the import is over
/// (<see cref="IAsepriteRecipe.Generate"/>, which rewrites only the sheets and sets that changed); other recipes are
/// left alone. What was written is logged, and the problems found as a warning on the recipe. Unity calls asset
/// postprocessors statically, so the queue is static.
/// </summary>
public sealed class AsepriteRegeneration : AssetPostprocessor
{
    private static readonly string[] RecipeTypes = { nameof(AsepriteCharacterRecipe), nameof(AsepriteEffectRecipe), nameof(AsepriteProjectileRecipe) };
    private static readonly HashSet<string> Pending = new();
    private static bool scheduled;

    /// <summary>
    /// The recipes queued for regeneration, by asset path.
    /// </summary>
    internal static IReadOnlyCollection<string> Queued => Pending;

    /// <summary>
    /// Queues the recipes using the Aseprite files among <paramref name="importedAssets"/>.
    /// </summary>
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        List<string> files = importedAssets.Where(p => p.EndsWith(AsepriteCharacterRecipe.Extension, StringComparison.OrdinalIgnoreCase) && AsepriteImportSettings.IsProjectArt(p)).ToList();
        if (files.Count > 0)
        {
            Queue(files);
        }
    }

    /// <summary>
    /// Queues every recipe asset reading one of <paramref name="files"/>, and schedules the regeneration for the
    /// editor's next update.
    /// </summary>
    internal static void Queue(IReadOnlyCollection<string> files)
    {
        HashSet<string> saved = new(files);
        foreach (string type in RecipeTypes)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{type}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) is IAsepriteRecipe recipe && recipe.FilePaths.Any(saved.Contains))
                {
                    Pending.Add(path);
                }
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
            if (!(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) is IAsepriteRecipe recipe))
            {
                continue;
            }

            Object asset = (Object)recipe;
            AsepriteReport report = recipe.Generate();
            generated.Add(path);
            if (report.Written.Count > 0)
            {
                Debug.Log($"Regenerated {asset.name} after its files were saved: {string.Join(", ", report.Written)}.", asset);
            }

            if (report.Problems.Count > 0)
            {
                Debug.LogWarning($"{asset.name} has {report.Problems.Count} problems:\n{string.Join("\n", report.Problems)}", asset);
            }
        }

        return generated;
    }
}
