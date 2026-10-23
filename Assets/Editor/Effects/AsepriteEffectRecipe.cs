using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// How an effect drawn in Aseprite is split into variants: one, its file named after its sheet, or one per damage type
/// in <see cref="DamageType"/>'s order (an impact), each file named after its sheet and its type.
/// </summary>
public enum AsepriteEffectVariants
{
    One,
    DamageTypes
}

/// <summary>
/// An editor-only recipe for a one-shot effect drawn by hand in Aseprite (Docs/AsepriteCharacters.md, Phase 3): the
/// sheet's name, which names its files and what it writes as the baked effect recipes' are named
/// (<see cref="EffectSheetWriter"/>, so an Aseprite recipe of a baked effect's name writes over it, keeping its slices'
/// IDs and its sprite set's GUID); its variants, one file each under <see cref="ArtRoot"/>:
/// <c>&lt;sheet&gt;.aseprite</c> for one, <c>&lt;sheet&gt;_&lt;damage type&gt;.aseprite</c> for one per damage type; whether
/// each is drawn in the eight directions (a tag each, E to SE) or facing one way (every frame of the file); and the pixel
/// its frames pivot on, from the canvas's bottom left. <see cref="AsepriteEffectWriter"/> copies the files' frames into
/// the sheet and writes its <see cref="EffectSprites"/>; the inspector previews a variant and generates.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Effects/Aseprite Effect Recipe", fileName = "AsepriteRecipe")]
public sealed class AsepriteEffectRecipe : ScriptableObject, IAsepriteRecipe
{
    public const string ArtRoot = "Assets/Art/Effects";

    [Tooltip("The sheet's name: its files are <sheet>.aseprite or <sheet>_<damage type>.aseprite, and it writes Assets/Sprites/Effects/<sheet>.png and <sheet>_Sprites.asset. Letters, digits and underscores, starting with a letter.")]
    [SerializeField] private string sheetName = "Effect";
    [Tooltip("One variant, or one per damage type in their order (an impact).")]
    [SerializeField] private AsepriteEffectVariants variants;
    [Tooltip("Drawn in the eight directions, a tag each named E, NE, N, NW, W, SW, S and SE over the same frames; otherwise facing one way, every frame of the file.")]
    [SerializeField] private bool directional = true;
    [Tooltip("The pixel every frame pivots on, from the canvas's bottom left: an impact's middle, a death puff's feet.")]
    [SerializeField] private Vector2Int pivot = new(20, 20);
    [Tooltip("The variants' files, in the variants' order, under Assets/Art/Effects; every frame of every file the same size and duration.")]
    [SerializeField] private List<Object> files = new();

    public string SheetName => sheetName;
    public AsepriteEffectVariants Variants => variants;
    public bool Directional => directional;
    public Vector2Int Pivot => pivot;
    public IReadOnlyList<Object> Files => files;
    public int VariantCount => variants == AsepriteEffectVariants.DamageTypes ? Enum.GetValues(typeof(DamageType)).Length : 1;
    public int DirectionCount => directional ? CharacterDirections.Count : 1;
    public IEnumerable<string> FilePaths => files.Where(f => f != null).Select(AssetDatabase.GetAssetPath);

    /// <summary>
    /// Creates an unsaved recipe, for tests and tools.
    /// </summary>
    public static AsepriteEffectRecipe Create(string name, AsepriteEffectVariants effectVariants, bool eightDirections, Vector2Int pivotPixel, IEnumerable<Object> variantFiles)
    {
        AsepriteEffectRecipe recipe = CreateInstance<AsepriteEffectRecipe>();
        recipe.sheetName = name;
        recipe.variants = effectVariants;
        recipe.directional = eightDirections;
        recipe.pivot = pivotPixel;
        recipe.files = new List<Object>(variantFiles);
        return recipe;
    }

    /// <summary>
    /// The name of variant <paramref name="variant"/>: its damage type's, or null for an effect of one variant.
    /// </summary>
    public string VariantName(int variant)
    {
        return variants == AsepriteEffectVariants.DamageTypes ? ((DamageType)variant).ToString() : null;
    }

    /// <summary>
    /// The file name variant <paramref name="variant"/>'s file must have.
    /// </summary>
    public string FileNameOf(int variant)
    {
        string name = VariantName(variant);
        return name == null ? sheetName + AsepriteCharacterRecipe.Extension : $"{sheetName}_{name}{AsepriteCharacterRecipe.Extension}";
    }

    /// <summary>
    /// The asset path of variant <paramref name="variant"/>'s file, or null when it has none.
    /// </summary>
    public string FilePathOf(int variant)
    {
        return variant < files.Count && files[variant] != null ? AssetDatabase.GetAssetPath(files[variant]) : null;
    }

    /// <summary>
    /// Whether the recipe can generate: a sheet name of letters, digits and underscores, a file per variant, each under
    /// <see cref="ArtRoot"/> and named <see cref="FileNameOf"/> (so a damage type's file cannot sit in another's place),
    /// and the pivot not below or left of the canvas (whether it lies inside is the files' to say). Returns the reason
    /// in <paramref name="problem"/> when it cannot.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        problem = null;
        if (!CharacterRecipe.IsName(sheetName, true))
        {
            problem = "the sheet's name must be letters, digits and underscores, starting with a letter.";
            return false;
        }

        int count = VariantCount;
        if (files.Count != count)
        {
            problem = count == 1 ? $"the recipe needs one file, {FileNameOf(0)}, but lists {files.Count}." : $"the recipe needs {count} files, one per damage type in their order ({string.Join(", ", Enum.GetNames(typeof(DamageType)))}), but lists {files.Count}.";
            return false;
        }

        for (int variant = 0; variant < count; variant++)
        {
            string path = FilePathOf(variant);
            string expected = FileNameOf(variant);
            if (string.IsNullOrEmpty(path) || Path.GetFileName(path) != expected)
            {
                problem = $"file {variant + 1} must be {expected}.";
                return false;
            }

            if (!path.StartsWith(ArtRoot + "/", StringComparison.Ordinal))
            {
                problem = $"{expected} must be under {ArtRoot}, where its import settings are kept.";
                return false;
            }
        }

        if (pivot.x < 0 || pivot.y < 0)
        {
            problem = "the pivot pixel cannot be negative.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes the sheet and its sprite set (<see cref="AsepriteEffectWriter.Generate"/>).
    /// </summary>
    public AsepriteReport Generate()
    {
        return AsepriteEffectWriter.Generate(this);
    }
}
