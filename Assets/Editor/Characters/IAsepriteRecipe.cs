using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only recipe that writes sheets from <c>.aseprite</c> files (Docs/AsepriteCharacters.md): a character's
/// (<see cref="AsepriteCharacterRecipe"/>), an effect's (<see cref="AsepriteEffectRecipe"/>) or a projectile's
/// (<see cref="AsepriteProjectileRecipe"/>). <see cref="AsepriteRegeneration"/> generates every one using a file that was
/// saved.
/// </summary>
public interface IAsepriteRecipe
{
    /// <summary>
    /// The asset paths of the files the recipe reads.
    /// </summary>
    IEnumerable<string> FilePaths { get; }

    /// <summary>
    /// Reads the files and writes what changed, reporting the problems found and the paths written.
    /// </summary>
    AsepriteReport Generate();
}

/// <summary>
/// What a run of an Aseprite recipe's Generate did: the problems found, each naming the file, tag and frame where it
/// has them, and the paths it wrote (a sheet, an animation set or a sprite set that changed).
/// </summary>
public sealed class AsepriteReport
{
    public List<string> Problems { get; } = new();
    public List<string> Written { get; } = new();

    /// <summary>
    /// Logs what a Generate of <paramref name="recipe"/> wrote, as a warning listing the problems when it found any,
    /// and returns the problems as one text (empty without any), which the recipes' inspectors show.
    /// </summary>
    public string LogGenerated(Object recipe)
    {
        string problems = string.Join("\n", Problems);
        string written = Written.Count > 0 ? string.Join(", ", Written) : "nothing changed";
        if (Problems.Count > 0)
        {
            Debug.LogWarning($"Generated {recipe.name} ({written}) with {Problems.Count} problems:\n{problems}", recipe);
        }
        else
        {
            Debug.Log($"Generated {recipe.name}: {written}.", recipe);
        }

        return problems;
    }
}
