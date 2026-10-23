using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// An editor-only recipe for a projectile drawn by hand in Aseprite (Docs/AsepriteCharacters.md, Phase 3), as the bow's
/// arrow is baked by its <see cref="WeaponSpriteRecipe"/>: the sheet's name, which names its file
/// (<c>&lt;sheet&gt;.aseprite</c> under <see cref="ArtRoot"/>) and what it writes as the weapon sprite writer names the
/// arrow's (<see cref="WeaponSpriteWriter.WriteDirections"/>, so a recipe of the baked arrow's name writes over it,
/// keeping its slices' IDs and its set's GUID); how many directions it is drawn in, one frame each, frame 1 facing east
/// and each next one a step counterclockwise; and its tip, the pixel every frame pivots on (where it flies), from the
/// canvas's bottom left. <see cref="AsepriteProjectileWriter"/> copies the frames into a sheet and writes its
/// <see cref="DirectionalSprites"/>; the inspector previews it and generates.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Aseprite Projectile Recipe", fileName = "AsepriteRecipe")]
public sealed class AsepriteProjectileRecipe : ScriptableObject, IAsepriteRecipe
{
    public const string ArtRoot = "Assets/Art/Weapons";

    [Tooltip("The sheet's name: its file is <sheet>.aseprite, and it writes Assets/Sprites/Weapons/<sheet>.png and <sheet>_Directions.asset. Letters, digits and underscores, starting with a letter.")]
    [SerializeField] private string sheetName = "Arrow";
    [Tooltip("The file, <sheet>.aseprite under Assets/Art/Weapons: one frame per direction, frame 1 facing east and each next one a step counterclockwise; tags are ignored.")]
    [SerializeField] private Object file;
    [Tooltip("How many directions it is drawn in, evenly spaced counterclockwise from east: the file's frame count.")]
    [SerializeField, Min(1)] private int directions = 32;
    [Tooltip("The tip, the pixel every frame pivots on (where the projectile flies), from the canvas's bottom left.")]
    [SerializeField] private Vector2Int tip = new(28, 28);

    public string SheetName => sheetName;
    public Object File => file;
    public string FilePath => file != null ? AssetDatabase.GetAssetPath(file) : null;
    public int Directions => directions;
    public Vector2Int Tip => tip;
    public IEnumerable<string> FilePaths => file != null ? new[] { FilePath } : Array.Empty<string>();

    /// <summary>
    /// Creates an unsaved recipe, for tests and tools.
    /// </summary>
    public static AsepriteProjectileRecipe Create(string name, Object projectileFile, int directionCount, Vector2Int tipPixel)
    {
        AsepriteProjectileRecipe recipe = CreateInstance<AsepriteProjectileRecipe>();
        recipe.sheetName = name;
        recipe.file = projectileFile;
        recipe.directions = directionCount;
        recipe.tip = tipPixel;
        return recipe;
    }

    /// <summary>
    /// The file name the recipe's file must have.
    /// </summary>
    public string FileName => sheetName + AsepriteCharacterRecipe.Extension;

    /// <summary>
    /// Whether the recipe can generate: a sheet name of letters, digits and underscores, a file named
    /// <see cref="FileName"/> under <see cref="ArtRoot"/>, at least one direction and the tip not below or left of the
    /// canvas (whether it lies inside is the file's to say). Returns the reason in <paramref name="problem"/> when it
    /// cannot.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        problem = null;
        if (!CharacterRecipe.IsName(sheetName, true))
        {
            problem = "the sheet's name must be letters, digits and underscores, starting with a letter.";
            return false;
        }

        string path = FilePath;
        if (string.IsNullOrEmpty(path) || Path.GetFileName(path) != FileName)
        {
            problem = $"the recipe needs its file, {FileName}.";
            return false;
        }

        if (!path.StartsWith(ArtRoot + "/", StringComparison.Ordinal))
        {
            problem = $"{FileName} must be under {ArtRoot}, where its import settings are kept.";
            return false;
        }

        if (directions < 1)
        {
            problem = "the projectile needs at least one direction.";
            return false;
        }

        if (tip.x < 0 || tip.y < 0)
        {
            problem = "the tip pixel cannot be negative.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes the sheet and its direction set (<see cref="AsepriteProjectileWriter.Generate"/>).
    /// </summary>
    public AsepriteReport Generate()
    {
        return AsepriteProjectileWriter.Generate(this);
    }
}
